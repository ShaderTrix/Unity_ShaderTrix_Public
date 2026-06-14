using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Unity.Collections;

public static class BVHEngine
{
    public static (List<BVHStructure.BvhData> bvhDatas,
                   List<BVHStructure.Triangle> triangles)
                   BuildBvh(GameObject rootMeshObject,
                            int splitCount)
    {
        var triangles = CreateTriangle(rootMeshObject);
        var rootNode = CreateBVH(triangles, splitCount);

        var (bvhDataList, triangleIndexes) = CreateBVHDatas(rootNode);
        var sortedTriangles = triangleIndexes.Select(x => triangles[x]).ToList();
        return (bvhDataList, sortedTriangles);
    }

    //stores every vertex and its index in a flat buffer for every obj under the given gameobject into a Flat 1D array
    public static List<BVHStructure.Triangle> CreateTriangle(GameObject obj)
    {
        var meshFilters = obj.GetComponentsInChildren<MeshFilter>(false);
        return meshFilters.SelectMany(x =>//SelectMany is different from Select aas it dosnt preserve groupings into arrays
                                          //Selectmany from a list = IEnumerable x = {[],[],[]} so even if the slect function is returning since output still its an array
                                          //Select returns collection arrays while select many removes the arrays and simply puts everything in flat IEnumerable
        {
            var mesh = x.sharedMesh;
            var indices = mesh.triangles;
            var trans = x.transform;
            var worldVertices = mesh.vertices
            //perform transformPoint for each element in the array then convert to list 
                               .Select(x => trans.TransformPoint(x))
                               .ToList();//Converts the Ienumerator into list 

            //Enumarable.Range creates a range of elements between the given arguments (0,5) = (0,1,2,3,4,5)...
            return Enumerable.Range(0, indices.Length / 3).Select(i =>
            {
                var pos0 = worldVertices[indices[i * 3 + 0]];
                var pos1 = worldVertices[indices[i * 3 + 1]];
                var pos2 = worldVertices[indices[i * 3 + 2]];
                var normal = -Vector3.Cross(pos0 - pos1, pos2 - pos1).normalized;
                return new BVHStructure.Triangle()
                {
                    pos0 = pos0,
                    pos1 = pos1,
                    pos2 = pos2,
                    normal = normal
                };
            });
        }).ToList();//Select,SelectMany only return IEnumerable by default so we have to materialise it into normal List/arrays afterwards
        //Ienumerable is like a fake storage array/list it dosnt store anything onto memory but just groups elements together (faster)
    }
    private static BVHStructure.BvhNode CreateBVH(List<BVHStructure.Triangle> triangles, int splitCount)
    {
        BVHStructure.BvhNode rootNode;
        //Convert the selected array of triangle into a NativeArray (Job) , here defining the size of the NativeArray
        //Here we are giving the root node in a topdown tree traversal so we are giving the entire bounds then dividing into smaller BB
        var triBoundArray = new NativeArray<BVHStructure.TriangleBounds>(triangles.Count, Allocator.Temp);

        for (var i = 0; i < triangles.Count; i++)
        {
            var tri = triangles[i];
            var min = Vector3.Min(Vector3.Min(tri.pos0, tri.pos1), tri.pos2);
            var max = Vector3.Max(Vector3.Max(tri.pos0, tri.pos1), tri.pos2); //Create a bound using the given triangles
            var triBounds = new BVHStructure.TriangleBounds()
            {
                bounds = new Bounds()
                {
                    min = min,
                    max = max,
                },
                triangleIndex = i
            };
            triBoundArray[i] = triBounds;
        }
        rootNode = CreateBVHRecursive(triBoundArray, splitCount);
        triBoundArray.Dispose();
        return rootNode;
    }

    //If enough triangles (>=4), it tries to split: Tests different split planes along X/Y/Z.    
    // Chooses the split with the lowest cost (via SAH).
    // If a split is found → creates two child nodes and recurses.
    // If no good split → becomes a leaf node (contains a list of triangle indices).
    //Result: A hierarchical structure of bounding volumes.
    private static BVHStructure.BvhNode CreateBVHRecursive(NativeSlice<BVHStructure.TriangleBounds> triangleBoundsArray,
                                                           int splitCount,
                                                           int recursiveCount = 0)
    {
        float bestSplit = 0.0f;
        int bestAxis = -1;
        if (triangleBoundsArray.Length >= 4) // number of minimum triangles to split 
        {
            var (totalBounds, minCost) = CalculateBoundsAndSAH(triangleBoundsArray); // gives a bounds info and a mincost for that BB
            var size = totalBounds.size;

            //Define two split arrays as every branch will have at least 2 branches until it reaches the leaf node 
            var leftBuffer = new NativeArray<BVHStructure.TriangleBounds>(triangleBoundsArray.Length, Allocator.Temp);
            var rightBuffer = new NativeArray<BVHStructure.TriangleBounds>(triangleBoundsArray.Length, Allocator.Temp);

            for (int axis = 0; axis < 3; ++axis) // the axis here is X/Y/Z,decided in which direction it has to split
            {
                if (size[axis] < 0.001) continue;
                var step = size[axis] / (splitCount / (recursiveCount + 1));
                var stepStart = totalBounds.min[axis] + step;
                var stepEnd = totalBounds.max[axis] - step;

                for (float testSplit = stepStart; testSplit < stepEnd; testSplit += step)
                {
                    var (left, right) = SplitLR(triangleBoundsArray, axis, testSplit, ref leftBuffer, ref rightBuffer);
                    if (left.Length <= 1 || right.Length <= 1) continue;
                    var (_, cosLeft) = CalculateBoundsAndSAH(left);
                    var (_, cosRight) = CalculateBoundsAndSAH(right);
                    var cost = cosLeft + cosRight;
                    if (cost < minCost)
                    {
                        minCost = cost;
                        bestAxis = axis;
                        bestSplit = testSplit;
                    }
                }
            }
            rightBuffer.Dispose();
            leftBuffer.Dispose();
        }
        BVHStructure.BvhNode ret;
        if (bestAxis < 0)
        {
            ret = CreateBVHNodeLeaf(triangleBoundsArray);
        }
        else
        {
            var leftBuffer = new NativeArray<BVHStructure.TriangleBounds>(triangleBoundsArray.Length, Allocator.Temp);
            var rightBuffer = new NativeArray<BVHStructure.TriangleBounds>(triangleBoundsArray.Length, Allocator.Temp);
            {
                var (left, right) = SplitLR(triangleBoundsArray, bestAxis, bestSplit, ref leftBuffer, ref rightBuffer);
                var leftNode = CreateBVHRecursive(left, splitCount, recursiveCount + 1);
                var rightNode = CreateBVHRecursive(right, splitCount, recursiveCount + 1);

                var bounds = leftNode.bounds;
                bounds.Encapsulate(rightNode.bounds);
                ret = new BVHStructure.BvhNode()
                {
                    bounds = bounds,
                    left = leftNode,
                    right = rightNode
                };
            }
            rightBuffer.Dispose();
            leftBuffer.Dispose();
        }
        return ret;

        //there are too few triangles left to split again...so must be a leaf node..stops splitting and decalres leaf nodes and stops iterating
        static BVHStructure.BvhNode CreateBVHNodeLeaf(NativeSlice<BVHStructure.TriangleBounds> triangleBoundsArray)
        {
            return new BVHStructure.BvhNode()
            {
                bounds = CalculateBounds(triangleBoundsArray),
                triangleID = triangleBoundsArray.Select(x => x.triangleIndex).ToList()
            };
        }
    }

    private static Bounds CalculateBounds(NativeSlice<BVHStructure.TriangleBounds> triangleBoundsArray)
    {
        var min = Vector3.one * float.MaxValue;
        var max = Vector3.one * float.MinValue;

        for (int i = 0; i < triangleBoundsArray.Length; ++i)
        {
            var Bounds = triangleBoundsArray[i].bounds;
            min = Vector3.Min(min, Bounds.min);
            max = Vector3.Max(max, Bounds.max);
        }
        return new Bounds() { min = min, max = max };
    }

    //SAH(Surface area Heuristics) (Heuristics = not perfect but a probability)
    //the current BB has a cost of (number of triangles  * surfacearea) C = N * SA
    private static (Bounds, float) CalculateBoundsAndSAH(NativeSlice<BVHStructure.TriangleBounds> triangleBoundsArray)
    {
        var bounds = CalculateBounds(triangleBoundsArray);
        var size = bounds.size;
        var sah = triangleBoundsArray.Length * ((size.x * size.y) + (size.y * size.z) + (size.z * size.x));
        return (bounds, sah);
    }

    private static (NativeSlice<BVHStructure.TriangleBounds> left,
                    NativeSlice<BVHStructure.TriangleBounds> right)
    SplitLR(NativeSlice<BVHStructure.TriangleBounds> triangleBoundsArray,
            int axis,
            float split,
            ref NativeArray<BVHStructure.TriangleBounds> leftBuffer,
            ref NativeArray<BVHStructure.TriangleBounds> rightBuffer)
    {
        int leftCount = 0; int rightCount = 0;
        for (int i = 0; i < triangleBoundsArray.Length; i++)
        {
            var tb = triangleBoundsArray[i];
            if (tb.bounds.center[axis] < split)
            {
                leftBuffer[leftCount++] = tb;
            }
            else
            {
                rightBuffer[rightCount++] = tb;
            }
        }
        return (leftBuffer.Slice(0, leftCount), rightBuffer.Slice(0, rightCount));
    }
    private static (List<BVHStructure.BvhData>, List<int>) CreateBVHDatas(BVHStructure.BvhNode node)
    {
        var datas = new List<BVHStructure.BvhData>();
        var triangleIndex = new List<int>();
        CreateBVHDatasRecursive(node, datas, triangleIndex);
        return (datas, triangleIndex);
    }

    //Converts the recursive BvhNode tree into a flat list (List<BvhData>) and a triangle index buffer.
    // Each BvhData stores: Bounds (min/max) and Pointers to left/right child indices (or triangle index/count if it’s a leaf).
    //Result: A linearized BVH that can be uploaded to a buffer/texture for traversal.
    private static void CreateBVHDatasRecursive(BVHStructure.BvhNode node,
                                                List<BVHStructure.BvhData> datas,
                                                List<int> triangleIndices)
    {
        var data = new BVHStructure.BvhData()
        {
            min = node.bounds.min,
            max = node.bounds.max,
            leftIdx = -1,
            rightIdx = -1,
            triangleIdx = -1,
            triangleCount = 0
        };
        if (node.isLeaf)
        {
            var idx = triangleIndices.Count;
            triangleIndices.AddRange(node.triangleID);
            data.triangleIdx = idx;
            data.triangleCount = node.triangleID.Count;
            datas.Add(data);
        }
        else
        {
            data.triangleIdx = -1;
            var dataIdx = datas.Count;
            datas.Add(default);
            data.leftIdx = datas.Count;
            CreateBVHDatasRecursive(node.left, datas, triangleIndices);

            data.rightIdx = datas.Count;
            CreateBVHDatasRecursive(node.right, datas, triangleIndices);

            datas[dataIdx] = data;
        }
    }    
}
