using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SoftBodySimulation
{
    public sealed partial class MeshBasedCloth : BaseClothSimulation
    {
        [SerializeField] private float _minimumGroupDistance = 1f;
        [SerializeField] private int _maxAmountOfParticles = 250;
        private Vector3 axisScaling = Vector3.one;
        protected override void Start()
        {
            base.Start();
            var mf = GetComponent<MeshFilter>();
            var mesh = mf.sharedMesh;

            List<Vector3> verts = new();
            mesh.GetVertices(verts);

            var localScale = transform.localScale;
            var localScaleAndVertices = verts.Select(x => Vector3.Scale(x, localScale)).ToArray();

            //cluster of random vertices logic (iterative spatial clustering)
            var meanInfo = new MeanVertexInfoTracker(mesh, localScaleAndVertices, axisScaling, _minimumGroupDistance, _maxAmountOfParticles);

            var bones = new List<Transform>();
            var springRadii = new List<float>();

            var minimalRadiiEverSeen = float.MaxValue;
            foreach (var meanPosition in meanInfo.MeanPositions)
            {
                var boneId = bones.Count;
                var boneObject = Instantiate(_bonePrefab, Vector3.zero, Quaternion.identity, transform);
                boneObject.transform.localPosition = meanPosition;
                boneObject.name = $"Bone {boneId}";

                // Find the closest other node, then the distance will be divided over the two nodes,
                // such that the half distance can become our radius.
                var scale = Mathf.Sqrt(meanInfo.GetClosestTo(meanPosition, index => index != boneId).Item2) / 2f;
                minimalRadiiEverSeen = Mathf.Min(minimalRadiiEverSeen, scale);

                bones.Add(boneObject.transform);
                springRadii.Add(scale);

                _springProcessor.AddSpringNode(boneObject.transform, scale);
            }
            var createSpringDamperTuples = new HashSet<(int, int)>();

            void CreateSpringDamper(SpringDamperType type, int firstIdx, int secondIdx)
            {
                if (secondIdx == -1) return;
                if (firstIdx == secondIdx) return;

                createSpringDamperTuples.Add(MeanVertexInfoTracker.SortTuple(firstIdx, secondIdx));
                var firstStartLocation = bones[firstIdx].position;
                var secondStartLocation = bones[secondIdx].position;
                _springProcessor.AddSpringDamper(type, Vector3.Distance(firstStartLocation, secondStartLocation),
                    firstIdx, secondIdx);
            }

            var directionStretching = minimalRadiiEverSeen / 2.5f;
            for (int i = 0; i < meanInfo.MeanPositions.Count; ++i)
            {
                var meanPosition = meanInfo.MeanPositions[i];
                var meanTangent = meanInfo.MeanTangents[i];
                var meanNormal = meanInfo.MeanNormals[i];

                var perpTangent = Vector3.Cross(meanTangent, meanNormal);

                var target = meanInfo.GetBestScoringInDirection(meanPosition + meanTangent * directionStretching,
                        meanTangent.normalized,
                        i, createSpringDamperTuples);
                // if (target == -1) continue;
                // if (target < 0 || target >= bones.Count) continue;
                CreateSpringDamper(SpringDamperType.MeshElastic, target, i);

                var target2 = meanInfo.GetBestScoringInDirection(meanPosition + perpTangent * directionStretching,
                        perpTangent.normalized,
                        i, createSpringDamperTuples);
                // if (target2 == -1) continue;
                // if (target2 < 0 || target2 >= bones.Count) continue;
                CreateSpringDamper(SpringDamperType.MeshElastic, target2, i);

                var mainDiagonal = (meanTangent + perpTangent).normalized;
                var notMainDiagonal = (meanTangent - perpTangent).normalized;

                // Shear springs
                var target3 = meanInfo.GetBestScoringInDirection(meanPosition + mainDiagonal * directionStretching,
                        mainDiagonal,
                        i, createSpringDamperTuples);
                // if (target3 == -1) continue;
                // if (target3 < 0 || target3 >= bones.Count) continue;  
                CreateSpringDamper(SpringDamperType.MeshShear,target3,i);

                var target4 = meanInfo.GetBestScoringInDirection(meanPosition + notMainDiagonal * directionStretching,
                        notMainDiagonal,
                        i, createSpringDamperTuples);
                // if (target4 == -1) continue;
                // if (target4 < 0 || target4 >= bones.Count) continue;
                CreateSpringDamper(SpringDamperType.MeshShear,target4,i);
            }

            var boneArrays = bones.ToArray();
            mesh.boneWeights = CalculateBoneWeights(meanInfo, boneArrays, springRadii.ToArray(), localScaleAndVertices);

            var mr = GetComponent<MeshRenderer>();
            var mat = mr.material;
            Destroy(mr);
            Destroy(mf);

            var smr = gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.material = mat;
            smr.updateWhenOffscreen = true;
            Initialize(mesh, boneArrays);
        }
        /// <summary>
        /// Calculate the bone weights for the given vertices and mean info.
        /// </summary>
        /// <param name="meanInfo">Information about the mean points, used for falling back when no appropriate weight can be found.</param>
        /// <param name="bones">The bones corresponding to the spring nodes.</param>
        /// <param name="radii">The radii corresponding to the spring nodes.</param>
        /// <param name="localScaledVertices">The vertices of the mesh scaled with the local scale.</param>
        /// <returns>The bone weights.</returns>
        private BoneWeight[] CalculateBoneWeights(MeanVertexInfoTracker meanInfo, Transform[] bones, float[] radii,
            Vector3[] localScaledVertices)
        {
            var boneWeights = new BoneWeight[localScaledVertices.Length];

            var mapVertexIdToWeights = new Dictionary<int, List<BoneWeight1>>();

            for (var springId = 0; springId < bones.Length; ++springId)
            {
                // Sphere of influence.
                // Factor 2 undoes the half-scaling done earlier.
                var sphereRadius = radii[springId] * 2f;
                var sqrSphereRadius = sphereRadius * sphereRadius;
                var sphereCentroid = bones[springId].localPosition;

                // Find all influences of this sphere.
                for (var vertexId = 0; vertexId < localScaledVertices.Length; ++vertexId)
                {
                    var difference = localScaledVertices[vertexId] - sphereCentroid;
                    var sqrDst = difference.sqrMagnitude;
                    if (sqrDst < sqrSphereRadius)
                    {
                        if (!mapVertexIdToWeights.TryGetValue(vertexId, out var weights))
                            mapVertexIdToWeights.Add(vertexId, weights = new List<BoneWeight1>());
                        var weight = (sphereRadius - Mathf.Sqrt(sqrDst)) / sphereRadius;
                        weights.Add(new BoneWeight1
                        {
                            boneIndex = springId,
                            weight = weight
                        });
                    }
                }
            }

            for (var vertexId = 0; vertexId < localScaledVertices.Length; ++vertexId)
            {
                if (!mapVertexIdToWeights.TryGetValue(vertexId, out var weights))
                {
                    // Fallback.
                    boneWeights[vertexId] = new BoneWeight
                    {
                        boneIndex0 = meanInfo.GetClosestTo(localScaledVertices[vertexId]).Item1,
                        weight0 = 1f
                    };
                    continue;
                }

                weights.Sort((a, b) => b.weight.CompareTo(a.weight));
                boneWeights[vertexId] = new BoneWeight
                {
                    boneIndex0 = weights[0].boneIndex,
                    weight0 = weights[0].weight,
                    boneIndex1 = weights.Count <= 1 ? 0 : weights[1].boneIndex,
                    weight1 = weights.Count <= 1 ? 0 : weights[1].weight,
                    boneIndex2 = weights.Count <= 2 ? 0 : weights[2].boneIndex,
                    weight2 = weights.Count <= 2 ? 0 : weights[2].weight,
                    boneIndex3 = weights.Count <= 3 ? 0 : weights[3].boneIndex,
                    weight3 = weights.Count <= 3 ? 0 : weights[3].weight
                };

                var totalWeight = boneWeights[vertexId].weight0 + boneWeights[vertexId].weight1 +
                                  boneWeights[vertexId].weight2 + boneWeights[vertexId].weight3;
                boneWeights[vertexId].weight0 /= totalWeight;
                boneWeights[vertexId].weight1 /= totalWeight;
                boneWeights[vertexId].weight2 /= totalWeight;
                boneWeights[vertexId].weight3 /= totalWeight;
            }

            return boneWeights;
        }
    }
}

