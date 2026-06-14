using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

public class BVHAsset : ScriptableObject
{
    public List<BVHStructure.BvhData> _bvhDataList;
    public List<BVHStructure.Triangle> _triangleList;

    // Create GPU buffers
    public (ComputeBuffer, ComputeBuffer) CreateBuffers()
    {
        var bvhBuffer = new ComputeBuffer(
            _bvhDataList.Count,
            Marshal.SizeOf(typeof(BVHStructure.BvhData)),
            ComputeBufferType.Structured
            );
        bvhBuffer.SetData(_bvhDataList);

        var triangleBuffer = new ComputeBuffer(
            _triangleList.Count,
            Marshal.SizeOf(typeof(BVHStructure.Triangle)),
            ComputeBufferType.Structured
            );
        triangleBuffer.SetData(_triangleList);
        
        return (bvhBuffer, triangleBuffer);
    }

    // Draw debug gizmos
    private void DrawBVHGizmo(int idx, int gizmoDepth, bool gizmoOnlyLeafNode, int recursiveCount = 0)
    {
        if (idx < 0 || _bvhDataList.Count <= idx) return;
        var data = _bvhDataList[idx];

        if (recursiveCount == gizmoDepth)
        {
            if (data.IsLeaf)
            {
                Gizmos.color = Color.red;
                for (int i = 0; i < data.triangleCount; ++i)
                {
                    var tri = _triangleList[i + data.triangleIdx];
                    Gizmos.DrawLine(tri.pos0, tri.pos1);
                    Gizmos.DrawLine(tri.pos0, tri.pos2);
                    Gizmos.DrawLine(tri.pos1, tri.pos2);
                }
            }
            if (!gizmoOnlyLeafNode || data.IsLeaf)
            {
                var bounds = new Bounds { min = data.min, max = data.max };
                Gizmos.color = data.IsLeaf ? Color.cyan : Color.green;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }
        }
        else if (!data.IsLeaf)
        {
            DrawBVHGizmo(data.leftIdx, gizmoDepth, gizmoOnlyLeafNode, recursiveCount + 1);
            DrawBVHGizmo(data.rightIdx, gizmoDepth, gizmoOnlyLeafNode, recursiveCount + 1);
        }
    }

    public void DrawGizmo(int gizmoDepth, bool gizmoOnlyLeafNode = false)
    {
        if (_bvhDataList != null && _bvhDataList.Count > 0)
            DrawBVHGizmo(0, gizmoDepth, gizmoOnlyLeafNode);
    }

    // -------- Binary Save/Load -------- //

    // Save both nodes and triangles into one .bin file
    public static void SaveSceneToBin(string path,
        BVHStructure.BvhData[] nodes,
        BVHStructure.Triangle[] tris)
    {
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(nodes.Length);
            bw.Write(tris.Length);

            WriteArray(bw, nodes);
            WriteArray(bw, tris);
        }
    }

    // Load both arrays from one .bin file
    public static void LoadSceneFromBin(string path,
        out BVHStructure.BvhData[] nodes,
        out BVHStructure.Triangle[] tris)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
        using (var br = new BinaryReader(fs))
        {
            int nodeCount = br.ReadInt32();
            int triCount = br.ReadInt32();

            nodes = ReadArray<BVHStructure.BvhData>(br, nodeCount);
            tris = ReadArray<BVHStructure.Triangle>(br, triCount);
        }
    }

    // Helpers to write entire array
    private static void WriteArray<T>(BinaryWriter bw, T[] data) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        int byteCount = size * data.Length;
        byte[] buffer = new byte[byteCount];

        IntPtr ptr = Marshal.AllocHGlobal(byteCount);
        for (int i = 0; i < data.Length; i++)
        {
            IntPtr dst = new IntPtr(ptr.ToInt64() + i * size);
            Marshal.StructureToPtr(data[i], dst, false);
        }
        Marshal.Copy(ptr, buffer, 0, byteCount);
        Marshal.FreeHGlobal(ptr);

        bw.Write(buffer);
    }

    public static T[] ReadArray<T>(BinaryReader br, int count) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] buffer = br.ReadBytes(size * count);

        T[] result = new T[count];
        IntPtr ptr = Marshal.AllocHGlobal(size * count);
        Marshal.Copy(buffer, 0, ptr, buffer.Length);

        for (int i = 0; i < count; i++)
        {
            IntPtr src = new IntPtr(ptr.ToInt64() + i * size);
            result[i] = Marshal.PtrToStructure<T>(src);
        }

        Marshal.FreeHGlobal(ptr);
        return result;
    }
}
