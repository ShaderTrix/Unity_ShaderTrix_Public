using UnityEngine;
using System.Runtime.InteropServices;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using TMPro;
using UnityEngine.Profiling;

public class IndirectRendererHelper : MonoBehaviour
{
    [System.Serializable]
    public struct LodData
    {
        public Mesh mesh;
        public string animationPath;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VertexAnimations
    {
        public Vector3 position;
        public Vector3 normals;
        public VertexAnimations(Vector3 pos, Vector3 norm)
        {
            position = pos;
            normals = norm;
        }
    }
    private int VERTEXANIMATIONBUFFER_STRIDE = Marshal.SizeOf(typeof(VertexAnimations));
    public FlowFieldAgentComputeHelper helperScript;
    [Header("LOD Meshes")]
    public List<LodData> _data;
    public Material material;
    private MaterialPropertyBlock[] _materialPropertyBlocks;


    [Header("LOD Distances")]
    public float lod1Distance = 20f;
    public float lod2Distance = 40f;

    private ComputeBuffer[] argsBuffers; // one per LOD
    private Bounds drawBounds;
    private float[] totalFrames, vertexCount;
    private static readonly int ARGS_STRIDE = 5 * sizeof(uint);
    private ComputeBuffer[] buffers; // animation buffers 
    private ComputeShader _visibilityShader;
    private ComputeBuffer[] _appendIndexBuffers; // per-LOD AppendStructuredBuffer<uint>
    private int _kernelID;
    [SerializeField] private TMP_Text debugText;
    [Header("FrustumCulling")]
    [SerializeField] private float _maxViewFalloff = 30f;
    // private Texture _cameraDepthTexture;
    // private RenderTexture _hierarchialDepthTexture;
    // private int MAXIMUM_BUFFER_SIZE = 1024;
    // private Vector2Int _hiZTexSize;
    // private CommandBuffer _hiZCommandBuffer;
    [Header("Occlusion")]
    [SerializeField][Range(0.0001f, 0.1f)] private float _depthBias = 0.001f;
    // caching
    private Camera _camera;
    private void Awake()
    {
        _camera = Camera.main;
        _camera.depthTextureMode = DepthTextureMode.Depth;

        drawBounds = new Bounds(Vector3.zero, Vector3.one * 10000f);

        totalFrames = new float[_data.Count];
        vertexCount = new float[_data.Count];

        InitShader();
        InitArgsBuffers();
        buffers = InitAnimationBuffers();
        _materialPropertyBlocks = Enumerable.Range(0, _data.Count).Select(_ => new MaterialPropertyBlock()).ToArray();        
    }
    private void InitShader()
    {
        if (!_visibilityShader)
        {
            string[] guids = AssetDatabase.FindAssets("IndirectRendererShader t:ComputeShader");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _visibilityShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            }
        }
        _kernelID = _visibilityShader.FindKernel("CSMain");
    }
    private void InitArgsBuffers()
    {
        ReleaseArgsBuffers();
        argsBuffers = _data.Select(x =>
        {
            var m = x.mesh;
            var args = new uint[5];
            args[0] = (uint)m.GetIndexCount(0);
            args[1] = 0; // instance count will be filled by GPU
            args[2] = (uint)m.GetIndexStart(0);
            args[3] = (uint)m.GetBaseVertex(0);
            args[4] = 0;

            var buffer = new ComputeBuffer(1, ARGS_STRIDE, ComputeBufferType.IndirectArguments);
            buffer.SetData(args);
            return buffer;
        }).ToArray();
    }
    private Vector4[] GetViewFrustumPlaneNormals()
    {
        Vector4[] planeNormals = new Vector4[6];
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(_camera);

        for (int i = 0; i < 6; i++)
        {
            planeNormals[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
        }
        return planeNormals;
    }
    // private void InitializeDepthTexture()
    // {
    //     if (_hierarchialDepthTexture != null)
    //     {
    //         _hierarchialDepthTexture.Release();
    //     }
    //     int size = (int)Mathf.Max((float)_camera.pixelWidth, (float)_camera.pixelWidth);
    //     size = (int)Mathf.Min((float)Mathf.NextPowerOfTwo(size), (float)MAXIMUM_BUFFER_SIZE);
    //     _hiZTexSize = new Vector2Int(size, size);

    //     _hierarchialDepthTexture = new RenderTexture(size, size, 0, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear);
    //     _hierarchialDepthTexture.filterMode = FilterMode.Point;
    //     _hierarchialDepthTexture.useMipMap = true;
    //     _hierarchialDepthTexture.autoGenerateMips = false;
    //     _hierarchialDepthTexture.Create();
    //     _hierarchialDepthTexture.hideFlags = HideFlags.HideAndDontSave;
    // }
    private uint[] ReadInstanceCounts()
    {
        uint[] counts = new uint[argsBuffers.Length];

        for (int i = 0; i < argsBuffers.Length; i++)
        {
            uint[] args = new uint[5];
            argsBuffers[i].GetData(args);
            counts[i] = args[1]; // THIS is the real instance count
        }

        return counts;
    }

    public void Render()
    {
        // Debug.Log(agentsBuffer.GetNativeBufferPtr());
        var buffer = helperScript._agentsBuffer;
        if (buffer == null) return;
        int agentCount = buffer.count;

        if (_visibilityShader == null) return;

        EnsureAppendBuffers(agentCount);

        for (int i = 0; i < _appendIndexBuffers.Length; i++)
            _appendIndexBuffers[i].SetCounterValue(0);

        _visibilityShader.SetBuffer(_kernelID, "_AgentsBuffer", buffer);
        material.SetBuffer("_AgentsBuffer", buffer);
        _visibilityShader.SetInt("_AgentCount", agentCount);
        _visibilityShader.SetVector("_CameraPos", _camera.transform.position);
        _visibilityShader.SetFloat("_LodDist01", lod1Distance);
        _visibilityShader.SetFloat("_LodDist02", lod2Distance);
        _visibilityShader.SetVectorArray("_ViewFrustumPlanes",GetViewFrustumPlaneNormals());
        _visibilityShader.SetFloat("_MaxVertexDistance", _maxViewFalloff);
        _visibilityShader.SetFloat("_DepthBias", _depthBias);

        // Matrix4x4 vpMatrix = GL.GetGPUProjectionMatrix(_camera.projectionMatrix, false) * _camera.worldToCameraMatrix;
        // _visibilityShader.SetMatrix("_VPMatrix", vpMatrix);

        // _cameraDepthTexture = Shader.GetGlobalTexture("_CameraDepthTexture");//URP auto creates and updates depth tex every frame just fetch it
        // if(_cameraDepthTexture)_visibilityShader.SetTexture(_kernelID,"_DepthTexture",_cameraDepthTexture);

        _visibilityShader.SetBuffer(_kernelID, "_AppendLOD0", _appendIndexBuffers[0]);
        _visibilityShader.SetBuffer(_kernelID, "_AppendLOD1", _appendIndexBuffers[1]);
        _visibilityShader.SetBuffer(_kernelID, "_AppendLOD2", _appendIndexBuffers[2]);

        // dispatch
        uint threadGroupX, _, __;
        _visibilityShader.GetKernelThreadGroupSizes(_kernelID, out threadGroupX, out _, out _);
        int groups = Mathf.CeilToInt((float)agentCount / (float)threadGroupX);

        _visibilityShader.Dispatch(_kernelID, groups, 1, 1);

        // Copy counters into indirect args buffers (offset 4 bytes -> args[1])
        ComputeBuffer.CopyCount(_appendIndexBuffers[0], argsBuffers[0], 4);
        ComputeBuffer.CopyCount(_appendIndexBuffers[1], argsBuffers[1], 4);
        ComputeBuffer.CopyCount(_appendIndexBuffers[2], argsBuffers[2], 4);

        var counts = ReadInstanceCounts();

        debugText.text =
            "Total Agents Requested = 20000\n"+
            $"LOD0 instances: {counts[0]}\n" +
            $"LOD1 instances: {counts[1]}\n" +
            $"LOD2 instances: {counts[2]}\n" +
            $"Total agents on screen: {counts[0] + counts[1] + counts[2]}";


        // Draw each LOD. Material must sample _InstanceIndexBuffer and then _AgentsBuffer[thatIndex]
        for (int i = 0; i < _data.Count; i++)
        {
            if (buffers[i] == null) continue;


            var mpb = _materialPropertyBlocks[i];
            mpb.SetBuffer("_VertexAnimationBuffer", buffers[i]);
            mpb.SetFloat("_NumOfFrames", totalFrames[i]);
            mpb.SetFloat("_TotalVertexCount", vertexCount[i]);


            // set the per-LOD instance index buffer in mpb so shader can read it
            if (i < _appendIndexBuffers.Length)
                mpb.SetBuffer("_InstanceIndexBuffer", _appendIndexBuffers[i]);


            var mesh = _data[i].mesh;
            Graphics.DrawMeshInstancedIndirect(mesh, 0, material, drawBounds, argsBuffers[i], 0, mpb);
        }
    }
    private void EnsureAppendBuffers(int agentCount)
    {
        if (_appendIndexBuffers != null && _appendIndexBuffers.Length == 3 && _appendIndexBuffers[0].count >= agentCount) return;


        // release old
        if (_appendIndexBuffers != null)
        {
            foreach (var b in _appendIndexBuffers) b?.Release();
        }


        _appendIndexBuffers = new ComputeBuffer[3];
        for (int i = 0; i < 3; i++)
        {
            // allocate worst-case capacity = agentCount indices
            _appendIndexBuffers[i] = new ComputeBuffer(agentCount, sizeof(uint), ComputeBufferType.Append);
        }
    }
    public ComputeBuffer[] InitAnimationBuffers()
    {
        return _data.Select((lod, i) =>
        {
            if (!Directory.Exists(lod.animationPath))
            {
                Debug.LogError($"Animation folder not found: {lod.animationPath}");
                return null;
            }

            var files = Directory.EnumerateFiles(lod.animationPath, "*.bytes")
                                 .OrderBy(f => f);

            List<VertexAnimations> allAnimData = new();
            int vCount = 0;
            int frames = 0;
            List<int> offsets = new();

            foreach (var file in files)
            {
                var (vertices, normals) = VertexAnimationBinaryBaker.LoadFromFile(
                    file, out int fileFrames, out int fileVertexCount);

                if (vCount == 0)
                {
                    vCount = fileVertexCount;
                }
                else if (vCount != fileVertexCount)
                {
                    Debug.LogWarning(
                        $"File {file} has a different vertex count ({fileVertexCount}). Expected {vertexCount}."
                    );
                }

                offsets.Add(frames);

                var animData = vertices
                    .SelectMany((frameVerts, frameIndex) =>
                        frameVerts.Select((v, vertIndex) =>
                            new VertexAnimations(v, normals[frameIndex][vertIndex])
                        )
                    );

                allAnimData.AddRange(animData);

                frames += fileFrames;
                Debug.Log($"Appended {fileFrames} frames from {Path.GetFileName(file)}");
            }
            totalFrames[i] = frames;
            vertexCount[i] = vCount;

            var buffer = new ComputeBuffer(
                allAnimData.Count,
                VERTEXANIMATIONBUFFER_STRIDE,
                ComputeBufferType.Structured
            );
            buffer.SetData(allAnimData);

            // TODO:offsets to store
            // animationOffsets = offsets.ToArray();

            return buffer;
        }).ToArray();
    }
    private void ReleaseArgsBuffers()
    {
        if (argsBuffers == null) return;
        for (int i = 0; i < argsBuffers.Length; i++)
        {
            argsBuffers[i]?.Release();
            argsBuffers[i] = null;
        }
    }


    private void OnDestroy()
    {
        ReleaseArgsBuffers();
        if (_appendIndexBuffers != null) foreach (var b in _appendIndexBuffers) b?.Release();
        if (buffers != null) foreach (var b in buffers) b?.Release();
    }
}
