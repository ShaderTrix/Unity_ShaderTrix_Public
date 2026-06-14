using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace SoftBodySimulation
{
    [RequireComponent(typeof(SkinnedMeshRenderer))]
    public class PBDPlaneMesh : PBDBaseCloth
    {
        [SerializeField] protected Material _material;
        [SerializeField, Range(8, 256)] protected int rows = 128, columns = 64;
        [SerializeField] protected int amountOfDivisionsInMesh = 16;
        [SerializeField] private float _windSpeed = 1.0f;
        [SerializeField] private float _windIntensity = 1.0f;  
        protected override void Start()
        {
            skm.sharedMaterial = _material;
            SetupTriangle();
            SetupGPUData();            
            base.Start();
        }
        private void SetupGPUData()
        {
            nodesCount = rows * columns;
            nodes = new PBDNode[nodesCount];
            var hCols = columns * 0.5f;
            //nodes
            for (int i = 0; i < nodesCount; ++i) nodes[i] = new PBDNode();

            for (int y = 0; y < rows; y++)
            {
                bool stable = (y == 0);
                var yoff = y * columns;

                for (int x = 0; x < columns; x++)
                {
                    var idx = yoff + x;
                    var n = nodes[idx];
                    n.position = n.prevPosition =
                        (Vector3.forward * y * _edgeLength) +
                        (Vector3.right * (x - hCols) * _edgeLength);
                    n.decay = 1f;
                    // n.pinned = (stable ? 1 : 0);
                    n.pinned = 0;
                    nodes[idx] = n;
                }
            }

            dampers = new List<PBDDamper>();
            for (int y = 0; y < rows; y++)
            {
                var yoff = y * columns;
                if (y != rows - 1)
                {
                    for (int x = 0; x < columns; x++)
                    {
                        var idx = yoff + x;
                        if (x != columns - 1)
                        {
                            var right = idx + 1;
                            dampers.Add(new PBDDamper(idx, right, _edgeLength));
                        }
                        var down = idx + columns;
                        dampers.Add(new PBDDamper(idx, down, _edgeLength));

                        // if (x < columns - 1 && y < rows - 1)
                        // {
                        //     int diag1 = idx + columns + 1;
                        //     int diag2 = idx + columns - 1;

                        //     dampers.Add(new PBDDamper(idx, diag1, _edgeLength * Mathf.Sqrt(2)));
                        //     if (x > 0)
                        //         dampers.Add(new PBDDamper(idx, diag2, _edgeLength * Mathf.Sqrt(2)));
                        // }
                    }
                }
                else
                {
                    for (int x = 0; x < columns - 1; x++)
                    {
                        var idx = yoff + x;
                        var right = idx + 1;
                        dampers.Add(new PBDDamper(idx, right, _edgeLength));
                    }
                }
                
            }
            pins = new PBDPin[nodesCount];

            damperCount = dampers.Count;
            _pbdProcessor = new PBDGPUProcessor(nodes, dampers.ToArray(),pins);
        }

        private void SetupTriangle()
        {
            // Grid dimensions must match GPU layout
            int cols = columns;
            int rowsLocal = rows;
            int vertsCount = rowsLocal * cols;
            float halfCols = cols * 0.5f;

            Mesh mesh = new Mesh();
            var vertices = new List<Vector3>(vertsCount);
            var uvs = new List<Vector2>(vertsCount);
            var triangles = new List<int>((rowsLocal - 1) * (cols - 1) * 6);
            var boneWeights = new List<BoneWeight>(vertsCount);
            var bones = new List<Transform>(vertsCount);

            // create vertices & uvs in exactly the same order as GPU nodes:
            // idx = x + y * columns
            for (int y = 0; y < rowsLocal; ++y)
            {
                float worldZ = y * _edgeLength;
                float v = (float)y / (rowsLocal - 1);
                for (int x = 0; x < cols; ++x)
                {
                    float worldX = (x - halfCols) * _edgeLength;
                    float u = (float)x / (cols - 1);
                    vertices.Add(new Vector3(worldX, 0f, worldZ));
                    uvs.Add(new Vector2(u, v));
                }
            }

            // build triangles using same column stride
            for (int y = 0; y < rowsLocal - 1; ++y)
            {
                for (int x = 0; x < cols - 1; ++x)
                {
                    int i0 = x + y * cols;
                    int i1 = (x + 1) + y * cols;
                    int i2 = x + (y + 1) * cols;
                    int i3 = (x + 1) + (y + 1) * cols;

                    // tri 1: i0, i2, i1
                    triangles.Add(i0);
                    triangles.Add(i2);
                    triangles.Add(i1);

                    // tri 2: i1, i2, i3
                    triangles.Add(i1);
                    triangles.Add(i2);
                    triangles.Add(i3);
                }
            }

            // create bones positioned exactly at vertex positions; one bone per vertex
            for (int i = 0; i < vertsCount; ++i)
            {
                var boneObj = Instantiate(_bonePrefab, transform);
                boneObj.name = $"Bone ({i})";
                boneObj.transform.localPosition = vertices[i];
                bones.Add(boneObj.transform);
            }

            // bone weights: direct 1-to-1 mapping (boneIndex0 = vertex index, weight0 = 1)
            for (int i = 0; i < vertsCount; ++i)
            {
                BoneWeight bw = new BoneWeight
                {
                    boneIndex0 = i,
                    weight0 = 1f,
                    boneIndex1 = 0,
                    weight1 = 0f,
                    boneIndex2 = 0,
                    weight2 = 0f,
                    boneIndex3 = 0,
                    weight3 = 0f
                };
                boneWeights.Add(bw);
            }

            // assign mesh
            if (vertices.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.boneWeights = boneWeights.ToArray();

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // bounds tuned for runtime rotation (same idea as before)
            mesh.bounds = new Bounds(
                new Vector3(0f, 0f, (rowsLocal - 1) * 0.5f * _edgeLength),
                new Vector3(cols * _edgeLength * 2f, rowsLocal * _edgeLength * 2f, rowsLocal * _edgeLength * 2f)
            );

            Initialize(mesh, bones.ToArray());
        }        
        protected override void Update()
        {
            base.Update();            
            _gravity.z = Mathf.PerlinNoise(Time.timeSinceLevelLoad * _windSpeed, 0f) * _windIntensity;
        }
    }
}
