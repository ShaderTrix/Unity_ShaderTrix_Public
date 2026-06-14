using System.Runtime.InteropServices;
using System.Collections.Generic;
using UnityEngine;


public class BVHStructure
{
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BvhData
    {
        public Vector3 min; //12b
        public float pad0;
        public Vector3 max; //12b
        public float pad1;

        public int leftIdx; //4b
        public int rightIdx; //4b

        public int triangleIdx; // -1 if data is not leaf //4b
        public int triangleCount; //4b

        public bool IsLeaf => triangleIdx >= 0;
    }
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Triangle
    {
        public Vector3 pos0; //12b
        public float pad0;
        public Vector3 pos1; //12b
        public float pad1;
        public Vector3 pos2; //12b
        public float pad2;
        public Vector3 normal; //12b
        public float pad3;
    }
    public struct TriangleBounds
    {
        public Bounds bounds;
        public int triangleIndex;
    }
    public class BvhNode
    {
        public Bounds bounds;
        public BvhNode left;
        public BvhNode right;
        public List<int> triangleID;
        public bool isLeaf => triangleID != null;
        //if triangleID is not null you have landed onto a leaf node for that recursive BB
    }
}
