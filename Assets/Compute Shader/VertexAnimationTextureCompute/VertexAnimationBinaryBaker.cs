using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class VertexAnimationBinaryBaker : MonoBehaviour
{
    public AnimationClip animationClip;
    public float sampleRate = 30f;

    [ContextMenu("Capture Animation To Binary")]
    private void CaptureAnimation()
    {
        if (animationClip == null)
        {
            Debug.LogError("No AnimationClip assigned!");
            return;
        }

        var smr = GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr == null)
        {
            Debug.LogError("No SkinnedMeshRenderer found!");
            return;
        }

        Animator animator = GetComponentInChildren<Animator>();
        if (animator == null)
        {
            Debug.LogError("No Animator found on this GameObject.");
            return;
        }

        string path = EditorUtility.SaveFilePanel(
            "Save Baked Animation Data",
            "",
            gameObject.name + "_" + animationClip.name + "_MeshData.bytes",
            "bytes");

        if (string.IsNullOrEmpty(path))
            return;

        float clipLength = animationClip.length;
        int totalFrames = Mathf.CeilToInt(clipLength * sampleRate);

        using (BinaryWriter writer = new BinaryWriter(File.Open(path, FileMode.Create)))
        {
            writer.Write(totalFrames);
            Mesh bakedMesh = new Mesh();

            for (int frame = 0; frame < totalFrames; frame++)
            {
                float time = frame / sampleRate;
                animationClip.SampleAnimation(gameObject, time);
                smr.BakeMesh(bakedMesh);

                Vector3[] vertices = bakedMesh.vertices;
                Vector3[] normals = bakedMesh.normals;

                writer.Write(frame);
                writer.Write(vertices.Length);

                for (int i = 0; i < vertices.Length; i++)
                {
                    writer.Write(vertices[i].x);
                    writer.Write(vertices[i].y);
                    writer.Write(vertices[i].z);

                    writer.Write(normals[i].x);
                    writer.Write(normals[i].y);
                    writer.Write(normals[i].z);
                }
            }
        }

        Debug.Log($"Baked animation '{animationClip.name}' with {totalFrames} frames to {path}");
#if UNITY_EDITOR
        AssetDatabase.Refresh();
#endif
    }

    /// <summary>
    /// Load baked animation data as two parallel lists:
    /// - vertices[frame] = Vector3[] of vertex positions for that frame
    /// - normals[frame] = Vector3[] of normals for that frame
    /// </summary>
    public static (List<Vector3[]> vertices, List<Vector3[]> normals) LoadFromFile(string path, out int totalFrames, out int vertexCount)
    {
        List<Vector3[]> verticesFrames = new List<Vector3[]>();
        List<Vector3[]> normalsFrames = new List<Vector3[]>();
        totalFrames = 0;
        vertexCount = 0;

        using (BinaryReader reader = new BinaryReader(File.Open(path, FileMode.Open)))
        {
            totalFrames = reader.ReadInt32();

            for (int f = 0; f < totalFrames; f++)
            {
                int frameIndex = reader.ReadInt32();
                int vCount = reader.ReadInt32();

                if (f == 0) vertexCount = vCount;

                if (vCount != vertexCount)
                {
                    Debug.LogError("Vertex count mismatch between frames!");
                    return (null, null);
                }

                Vector3[] verts = new Vector3[vCount];
                Vector3[] norms = new Vector3[vCount];

                for (int v = 0; v < vCount; v++)
                {
                    verts[v] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    norms[v] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }

                verticesFrames.Add(verts);
                normalsFrames.Add(norms);
            }
        }

        Debug.Log($"Loaded {totalFrames} frames × {vertexCount} verts.");
        return (verticesFrames, normalsFrames);
    }
}
