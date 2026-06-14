using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;

[ExecuteInEditMode]
public class AnimationTextureBaker : MonoBehaviour
{
    public enum OutputMode
    {
        TextureMode,
        BinaryFile,
    };

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct SourceVertex
    {
        public Vector3 position;
        public Vector3 normal;
    }
    [SerializeField] private OutputMode _output;
    [SerializeField] private ComputeShader _shader;
    [SerializeField] private string _path = "Assets/StreamingAssets/";
    [SerializeField] private AnimationClip[] _animationClips;
    [SerializeField] private int _fps = 5;

    void Start()
    {
        switch (_output)
        {
            case OutputMode.TextureMode:
                StartCoroutine(BakeAnimation());
                break;
            case OutputMode.BinaryFile:
                // BakeClipToBinary();
                break;
        }
    }

    IEnumerator BakeAnimation()
    {
        var animator = GetComponent<Animator>();
        var skin = GetComponentInChildren<SkinnedMeshRenderer>();
        var vertexCount = skin.sharedMesh.vertexCount;

        var mesh = new Mesh();
        mesh.MarkDynamic();

        //animator.speed = 0.0f;
        var textureWidth = Mathf.NextPowerOfTwo(vertexCount);

        foreach (var c in _animationClips)
        {
            int frames = Mathf.FloorToInt(c.length * _fps) + 1;
            var info = new List<SourceVertex>();

            RenderTexture positionRT = new RenderTexture(textureWidth, frames, 0, RenderTextureFormat.ARGBHalf);
            positionRT.name = name + "_positionTexture";
            RenderTexture normalRT = new RenderTexture(textureWidth, frames, 0, RenderTextureFormat.ARGBHalf);
            normalRT.name = name + "_normalTexture";

            foreach (var rt in new[] { positionRT, normalRT })
            {
                rt.enableRandomWrite = true;
                rt.Create();
                RenderTexture.active = rt;
                GL.Clear(true, true, Color.clear);
            }
            animator.Play(c.name);
            yield return 0;
            for (int i = 0; i < frames; i++)
            {
                animator.Play(c.name, 0, (float)i / frames);
                yield return 0;
                skin.BakeMesh(mesh);

                //This essentially make a new SourceVertex struct for every Vertex at every frame
                info.AddRange(Enumerable.Range(0, vertexCount).Select(idx => new SourceVertex()
                {
                    position = mesh.vertices[idx],
                    normal = mesh.normals[idx]
                }));
            }
            var buffer = new ComputeBuffer(info.Count, System.Runtime.InteropServices.Marshal.SizeOf(typeof(SourceVertex)));
            buffer.SetData(info);

            var kernel = _shader.FindKernel("CSMain");
            _shader.GetKernelThreadGroupSizes(kernel, out uint threadGroupSizeX, out uint threadGroupSizeY, out uint threadGroupSizeZ);
            _shader.SetInt("_vertexCount", vertexCount);
            _shader.SetBuffer(kernel, "_SourceVertex", buffer);
            _shader.SetTexture(kernel, "_outputPositions", positionRT);
            _shader.SetTexture(kernel, "_outputNormals", normalRT);
            _shader.Dispatch(kernel, vertexCount / (int)threadGroupSizeX + 1, frames / (int)threadGroupSizeY + 1, (int)threadGroupSizeZ);
            buffer.Release();

#if UNITY_EDITOR
            var positionTex = ConvertToTexture(positionRT);
            var normalTex = ConvertToTexture(normalRT);

            Graphics.CopyTexture(positionRT, positionTex);
            Graphics.CopyTexture(normalRT, normalTex);

            AssetDatabase.CreateAsset(positionTex, Path.Combine(_path, positionRT.name + ".asset"));
            Debug.Log("Texture 1 created");
            AssetDatabase.CreateAsset(normalTex, Path.Combine(_path, normalRT.name + ".asset"));
            Debug.Log("Texture 2 created");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
#endif
        }
        yield return null;
    }

    private Texture2D ConvertToTexture(RenderTexture rt)
    {
        var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBAHalf, false);
        RenderTexture.active = rt;
        texture.ReadPixels(Rect.MinMaxRect(0, 0, rt.width, rt.height), 0, 0);
        RenderTexture.active = null;
        return texture;
    }
}
