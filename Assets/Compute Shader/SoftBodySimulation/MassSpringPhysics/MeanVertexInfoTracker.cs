using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SoftBodySimulation
{
    public sealed class MeanVertexInfoTracker
    {
        public readonly List<Vector3> MeanPositions = new();
        public readonly List<Vector3> MeanTangents = new();
        public readonly List<Vector3> MeanNormals = new();

        /// <param name="mesh">The source mesh.</param>
        /// <param name="localScaledVertices">The locally scaled vertices according to scale and axis distance rescaling.</param>
        /// <param name="axisScaling">The axis scaling for distance calculation.</param>
        /// <param name="minimumGroupDistance">The minimum distance required between vertices to group together vertices.</param>
        /// <param name="maxParticles">Maximum amount of particles.</param>
        public MeanVertexInfoTracker(Mesh mesh, Vector3[] localScaledVertices, Vector3 axisScaling, float minGroupDist, int maxParticles)
        {
            //This functions picks the first vert from the mesh and checks its surrounding verts (mindist) and 
            //averages itsselfs and surrounding verts normals & tangent ,then it removes iteslf and surroundings from the list
            //then again picks the first from the list..because HashSet are random sorting in the list and not FIFO its random clusters
            //retuns how many it removed
            //Algorithm name : iterative spatial clustering
            List<Vector4> tangents = new();
            List<Vector3> normals = new();
            mesh.GetTangents(tangents);
            mesh.GetNormals(normals);

            var todo = new HashSet<(Vector3, int)>();
            for (int i = 0; i < localScaledVertices.Length; i++)
            {
                todo.Add((localScaledVertices[i], i));
            }

            // Attempt to select a vertex, create sphere around the vertex and repeat until no
            // spheres can be created or a maximum number of spheres is reached.
            while (maxParticles > 0 && todo.Count > 0)
            {
                var (sphereCentroid, _) = todo.First();
                var sphereRadius = minGroupDist;

                var meanNormals = Vector3.zero;
                var meanTangents = Vector3.zero;
                var count = todo.RemoveWhere(x =>
                {
                    var (position, index) = x;
                    var condition = Vector3.Scale(position - sphereCentroid, axisScaling).sqrMagnitude <= sphereRadius * sphereRadius;
                    if (condition)
                    {
                        meanNormals += normals[index];
                        var tangent = tangents[index];
                        meanTangents += new Vector3(tangent.x, tangent.y, tangent.z);
                    }
                    return condition;
                });
                meanNormals /= count;
                meanTangents /= count;

                // We want a position on a model vertex such that the spring more closely resembles the model.
                // But we want to keep the normals and tangents more aggregated because we will be using it
                // as a summary of the area when we make connections.
                MeanPositions.Add(sphereCentroid);
                MeanNormals.Add(meanNormals);
                MeanTangents.Add(meanTangents);

                --maxParticles;
            }
        }

        /// <summary>
        /// Get the closest (index, squared distance) tuple to a given vertex where the index fulfills a predicate.
        /// </summary>
        /// <param name="vector">The vector to measure against.</param>
        /// <param name="indexPredicate">The index predicate.</param>
        /// <returns>The (index, squared distance) tuple.</returns>
        public (int, float) GetClosestTo(Vector3 vector, Func<int, bool> indexPredicate)
        {
            //Func<int bool> int => iterator,bool => return
            //its basicall a filter which says if indexPredicate is false then ignore the current element in the list
            //eg:GetClosestTo(targetPos, i => false);//here it means exclude the current element from the calculation
            var dstSqr = float.MaxValue;
            var index = 0;
            for (int i = 0; i < MeanPositions.Count; i++)
            {
                if (!indexPredicate(i)) continue;//filter to exclude the element 
                var currDistSqr = (MeanPositions[i] - vector).sqrMagnitude;
                if (currDistSqr < dstSqr)
                {
                    dstSqr = currDistSqr;
                    index = i;
                }
            }
            return (index, dstSqr);
        }
        /// <summary>
        /// Get the closest (index, squared distance) tuple to a given vertex.
        /// </summary>
        /// <param name="vector">The vector to measure against.</param>
        /// <returns>The (index, squared distance) tuple.</returns>
        public (int, float) GetClosestTo(Vector3 vector) => GetClosestTo(vector, index => true);

        /// <summary>
        /// Sorts a tuple such that the first element is the smallest and the second element is the largest.
        /// </summary>
        /// <param name="a">An element of the tuple.</param>
        /// <param name="b">An element of the tuple.</param>
        /// <typeparam name="T"></typeparam>
        /// <returns>The sorted tuple.</returns>
        public static (T, T) SortTuple<T>(T a, T b) where T : IComparable
        {
            return Comparer<T>.Default.Compare(a, b) <= 0 ? (a, b) : (b, a);
        }
        /// <summary>
        /// Get the best scoring index closest to a vector in a given direction.
        /// </summary>
        /// <param name="vector">The starting point.</param>
        /// <param name="directionPreference">The given direction.</param>
        /// <param name="myIndex">The starting points index, which should not be allowed as a return value.</param>
        /// <param name="bannedTuples">Which tuples of indices are not allowed (myIndex, destinationIndex).</param>
        /// <returns>THe best scoring index.</returns>
        public int GetBestScoringInDirection(Vector3 vector, Vector3 directionPreference, int myIndex,
            HashSet<(int, int)> bannedTuples)
        {
            var sorted = MeanPositions
                        .Select((x, i) => ((x - vector).sqrMagnitude, i))     // produce (distance, index)
                        .OrderBy(x => x.sqrMagnitude)                        // sort by distance ASC
                        .Select(x => x.i)                                     // keep only the index
                        .ToArray();                                           // produce an array


            var maximumDot = -1f;
            var index = -1;

            var triedOut = 0;
            for (var i = 0; triedOut < 4 && i < sorted.Length; ++i)
            {
                var meanIndex = sorted[i];
                if (myIndex == meanIndex)
                    continue;
                
                //so baasically this means the HashSet who are like (3,2) & (2,3) mean the same connection so ignore them both
                //this step is to ignore double calculations for the same connection
                //Hashset gurantees no duplicate entries this guarentees no dublicates of interchanged places too
                if (bannedTuples.Contains(SortTuple(meanIndex, myIndex)))
                    continue;
                var dot = Vector3.Dot((MeanPositions[meanIndex] - vector).normalized, directionPreference);
                if (dot > maximumDot)
                {
                    maximumDot = dot;
                    index = meanIndex;
                }

                ++triedOut;//basically it reutns the fisrt 4 elements which have similar directions to the given preferred direciton
                //filtering out the underisred indices and itself from the comparision
            }

            return index;
        }
    }
}
