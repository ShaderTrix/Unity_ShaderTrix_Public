using System;
using System.Runtime.InteropServices;
using TMPro;
using UnityEditor;
using UnityEngine;

public class PixelizerDrawCubes : MonoBehaviour
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CubeData
    {
        public Vector4 position;
        public Vector4 scale;
        public Vector4 color;
        public Vector2 uv;
        public int id;
        public float padding;
    }
    private ComputeBuffer aliveBuffer;          // StructuredBuffer<CubeData>
    private ComputeBuffer aliveCounterBuffer;   // StructuredBuffer<uint>[1]
    private ComputeBuffer aliveArgsBuffer;      // Indirect args

    [StructLayout(LayoutKind.Sequential)]
    public struct CubePhysicsData
    {
        public Vector4 position;
        public Vector4 scale;
        public Vector4 color;
        public Vector4 velocity;
    }
    private ComputeBuffer deadBuffer;          // StructuredBuffer<CubeData>
    private ComputeBuffer deadCounterBuffer;   // StructuredBuffer<uint>[1]
    private ComputeBuffer deadArgsBuffer;      // Indirect args

    [StructLayout(LayoutKind.Sequential)]
    public struct AccumalatorData
    {
        public uint posXSum, posYSum, posZSum;
        public uint colorXsum, colorYsum, colorZsum;
        public uint count;
        public uint minZ;
        public uint idSum;
        public Vector3 padding;
    }
    private ComputeBuffer accumBuffer;

    [SerializeField] private TMP_Text _uiText1,_uiText2;
    private ComputeShader _computeShader;
    private Material _material01;
    private Material _material02;
    private Mesh cubeMesh;

    private int clearAccumKernel;
    private int accumIdKernel;
    private int aliveBlockKernel;
    private int transferBlockKernel;
    private int cubePhysicsKernel;

    private RenderTexture inputColorRT;
    private RenderTexture inputWposRT;

    private readonly uint[] counterReadback = new uint[1];
    private readonly uint[] indirectArgs = new uint[5];

    private Vector2Int cachedTexSize;
    private uint cachedBlockSize;

    [SerializeField] private uint _blockSize = 64;

    private int accumGroupsX;
    private int accumGroupsY;
    private int outputGroups;

    void Start()
    {
        _computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            AssetDatabase.GUIDToAssetPath(
                AssetDatabase.FindAssets("PizelizerCubesCompute t:ComputeShader")[0]
            )
        );

        _material01 = new Material(Shader.Find("Hidden/PixelizerCubesGraphics"));
        _material02 = new Material(Shader.Find("Hidden/PixelizerCubesGraphics"));

        clearAccumKernel = _computeShader.FindKernel("ClearAccum");
        accumIdKernel = _computeShader.FindKernel("AccumIDs");
        aliveBlockKernel = _computeShader.FindKernel("AliveBlocks");
        transferBlockKernel = _computeShader.FindKernel("TransferBlocks");
        cubePhysicsKernel = _computeShader.FindKernel("CubePhysics");

        InitCubeMesh();

        aliveArgsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        deadArgsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
    }

    void LateUpdate()
    {
        if (!PixelizerGunRendererFeatureMRT.MRTRenderTextureProvider.hasRenderedRT)
            return;

        inputColorRT = PixelizerGunRendererFeatureMRT.MRTRenderTextureProvider.colorCapturedRT;
        inputWposRT = PixelizerGunRendererFeatureMRT.MRTRenderTextureProvider.wPosCapturedRT;

        if (inputColorRT.width != cachedTexSize.x ||
            inputColorRT.height != cachedTexSize.y)
        {
            InitializeOutputBuffers(inputColorRT);
        }

        if (_blockSize != cachedBlockSize)
        {
            InitializeAccumBuffer();
        }

        // --- Reset counter BEFORE dispatch ---
        aliveCounterBuffer.SetData(new uint[] { 0 });//reset counter which counts how many cubes are there in the alive buffer
        // deadCounterBuffer.SetData(new uint[] { 0 });  

        SetupDispatchSizes();
        UpdateShaderBindings();

        _computeShader.Dispatch(clearAccumKernel, outputGroups, 1, 1);
        _computeShader.Dispatch(accumIdKernel, accumGroupsX, accumGroupsY, 1);
        _computeShader.Dispatch(aliveBlockKernel, outputGroups, 1, 1);
        _computeShader.Dispatch(transferBlockKernel, outputGroups, 1, 1);


        // --- CPU readback (intentional stall) ---
        aliveCounterBuffer.GetData(counterReadback);
        uint aliveCount = (uint)Mathf.Min(counterReadback[0], _blockSize * _blockSize);        

        // --- Build indirect args ---
        indirectArgs[0] = cubeMesh.GetIndexCount(0);
        indirectArgs[1] = aliveCount;
        indirectArgs[2] = cubeMesh.GetIndexStart(0);
        indirectArgs[3] = cubeMesh.GetBaseVertex(0);
        indirectArgs[4] = 0;

        aliveArgsBuffer.SetData(indirectArgs);

        deadCounterBuffer.GetData(counterReadback);
        uint deadCount = counterReadback[0];

        uint[] deadArgs = new uint[5];
        deadArgs[0] = cubeMesh.GetIndexCount(0);
        deadArgs[1] = deadCount;
        deadArgs[2] = cubeMesh.GetIndexStart(0);
        deadArgs[3] = cubeMesh.GetBaseVertex(0);
        deadArgs[4] = 0;
        deadArgsBuffer.SetData(deadArgs);
        

        _computeShader.GetKernelThreadGroupSizes(
            cubePhysicsKernel, out uint outTGX, out _, out _
        );
        if (deadCount > 0)
        {
            _computeShader.Dispatch(cubePhysicsKernel,(int)((deadCount + outTGX - 1) / outTGX),1,1);
        }    

        _uiText1.text = 
        $"Buffers:\nCubesbuffer:\nSize = {aliveBuffer.stride} bytes \nCount = {aliveCount}\n"+
        $"Physicsbuffer:\nSize = {deadBuffer.stride} bytes \nCount = {deadCount}";

        _uiText2.text =
        $"Kernels:\nClearAccumKernel (DispatchSize : {outputGroups})\n"+
        $"AccumIdKernel (DispatchSize) :{accumGroupsX} x {accumGroupsY})\n"+
        $"AliveBlockKernel (DispatchSize : {outputGroups})\n"+
        $"TransferBlockKernel (DispatchSize : {outputGroups})\n"+
        $"CubePhysicsKernel (DispatchSize : {(int)((deadCount + outTGX - 1) / outTGX)})";

        // var mpb = new MaterialPropertyBlock();
        Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        _material01.SetBuffer("_AliveBuffer", aliveBuffer);
        _material01.SetFloat("_Facing",0.9f);
        Graphics.DrawMeshInstancedIndirect(
            cubeMesh,
            0,
            _material01,
            bounds,
            aliveArgsBuffer,
            0,
            null,
            UnityEngine.Rendering.ShadowCastingMode.On,
            true
        );
        _material02.SetFloat("_Facing",0.0f);
        _material02.SetBuffer("_AliveBuffer", deadBuffer);
        Graphics.DrawMeshInstancedIndirect(
            cubeMesh,
            0,
            _material02,
            bounds,
            deadArgsBuffer,
            0,
            null,
            UnityEngine.Rendering.ShadowCastingMode.On,
            true                
        );
    }
    public void KillEnemy(float enemyID)
    {
        _computeShader.SetFloat("_KillEnemyID", enemyID);
    }

    // ------------------------------------------------------------------------

    void SetupDispatchSizes()
    {
        _computeShader.GetKernelThreadGroupSizes(
            accumIdKernel, out uint tgx, out uint tgy, out _
        );

        accumGroupsX = (inputColorRT.width + (int)tgx - 1) / (int)tgx;
        accumGroupsY = (inputColorRT.height + (int)tgy - 1) / (int)tgy;

        _computeShader.GetKernelThreadGroupSizes(
            aliveBlockKernel, out uint outTGX, out _, out _
        );

        uint blockCount = _blockSize * _blockSize;
        outputGroups = (int)((blockCount + outTGX - 1) / outTGX);
    }

    void UpdateShaderBindings()
    {
        _computeShader.SetBuffer(clearAccumKernel, "_AccumBuffer", accumBuffer);

        _computeShader.SetTexture(accumIdKernel, "_inputColorBuffer", inputColorRT);
        _computeShader.SetTexture(accumIdKernel, "_inputPositionBuffer", inputWposRT);
        _computeShader.SetBuffer(accumIdKernel, "_AccumBuffer", accumBuffer);

        _computeShader.SetBuffer(aliveBlockKernel, "_AccumBuffer", accumBuffer);
        _computeShader.SetBuffer(aliveBlockKernel, "_AliveBuffer", aliveBuffer);
        _computeShader.SetBuffer(aliveBlockKernel, "_AliveCounter", aliveCounterBuffer);

        _computeShader.SetBuffer(transferBlockKernel, "_AliveBuffer", aliveBuffer);
        _computeShader.SetBuffer(transferBlockKernel, "_DeadBuffer", deadBuffer);
        _computeShader.SetBuffer(transferBlockKernel, "_AliveCounter", aliveCounterBuffer);
        _computeShader.SetBuffer(transferBlockKernel, "_DeadCounter", deadCounterBuffer);

        _computeShader.SetBuffer(cubePhysicsKernel, "_DeadBuffer", deadBuffer);
        _computeShader.SetBuffer(cubePhysicsKernel, "_DeadCounter", deadCounterBuffer);
        
        _computeShader.SetVector("_Gravity", new Vector3(0, -9.81f, 0));
        _computeShader.SetFloat("_DeltaTime", Time.deltaTime);
        _computeShader.SetFloat("_Damping", 0.99f);
        Camera cam = Camera.main;
        _computeShader.SetVector("_CameraPos", cam.transform.position);
        _computeShader.SetVector("_CameraForward", cam.transform.forward);
        _computeShader.SetFloat("_VerticalFOV", cam.fieldOfView * Mathf.Deg2Rad);
        _computeShader.SetVector("_TexSize", new Vector2(inputColorRT.width, inputColorRT.height));
        _computeShader.SetFloat("_BlockSize", _blockSize);
    }

    void InitializeOutputBuffers(RenderTexture rt)
    {
        cachedTexSize = new Vector2Int(rt.width, rt.height);

        int maxCubes = (int)(_blockSize * _blockSize);
        const int MAX_DEAD_CUBES = 200000;

        aliveBuffer?.Release();
        aliveBuffer = new ComputeBuffer(
            maxCubes,
            Marshal.SizeOf<CubeData>(),
            ComputeBufferType.Structured
        );

        deadBuffer?.Release();
        deadBuffer = new ComputeBuffer(
            MAX_DEAD_CUBES,
            Marshal.SizeOf<CubePhysicsData>(),
            ComputeBufferType.Structured
        );

        aliveCounterBuffer?.Release();
        aliveCounterBuffer = new ComputeBuffer(
            1,
            sizeof(uint),
            ComputeBufferType.Structured
        );
        deadCounterBuffer?.Release();
        deadCounterBuffer = new ComputeBuffer(
            1,
            sizeof(uint),
            ComputeBufferType.Structured
        );
        deadCounterBuffer.SetData(new uint[] { 0 });  
    }

    void InitializeAccumBuffer()
    {
        cachedBlockSize = _blockSize;

        int count = (int)(_blockSize * _blockSize);

        accumBuffer?.Release();
        accumBuffer = new ComputeBuffer(
            count,
            Marshal.SizeOf<AccumalatorData>(),
            ComputeBufferType.Structured
        );
    }
    void InitCubeMesh()
    {
        // var vertArray = new Vector3[4 + 4 + 4 + 4 + 4];
        Vector3[] vertArray =
        {
            // Front face
            new Vector3(-1,-1,1) * 0.5f,
            new Vector3(1,-1,1) * 0.5f,
            new Vector3(1,1,1) * 0.5f,
            new Vector3(-1,1,1) * 0.5f,

            // Back face
            new Vector3(1,-1,-1) * 0.5f,
            new Vector3(-1,-1,-1) * 0.5f,
            new Vector3(-1,1,-1) * 0.5f,
            new Vector3(1,1,-1) * 0.5f,

            // Left
            new Vector3(-1,-1,-1) * 0.5f,
            new Vector3(-1,-1,1) * 0.5f,
            new Vector3(-1,1,1) * 0.5f,
            new Vector3(-1,1,-1) * 0.5f,

            // Right
            new Vector3(1,-1,1) * 0.5f,
            new Vector3(1,-1,-1) * 0.5f,
            new Vector3(1,1,-1) * 0.5f,
            new Vector3(1,1,1) * 0.5f,

            // Top
            new Vector3(-1,1,1) * 0.5f,
            new Vector3(1,1,1) * 0.5f,
            new Vector3(1,1,-1) * 0.5f,
            new Vector3(-1,1,-1) * 0.5f,

            // Bottom
            new Vector3(-1,-1,-1) * 0.5f,
            new Vector3(1,-1,-1) * 0.5f,
            new Vector3(1,-1,1) * 0.5f,
            new Vector3(-1,-1,1) * 0.5f
        };

        Vector3[] normalArray =
        {
            // Front
            Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
            // Back
            Vector3.back, Vector3.back, Vector3.back, Vector3.back,
            // Left
            Vector3.left, Vector3.left, Vector3.left, Vector3.left,
            // Right
            Vector3.right, Vector3.right, Vector3.right, Vector3.right,
            // Top
            Vector3.up, Vector3.up, Vector3.up, Vector3.up,
            // Bottom
            Vector3.down, Vector3.down, Vector3.down, Vector3.down
        };


        int[] triangleArray =
        {
            // FRONT (+Z)
            0,1,2,   0,2,3,

            // BACK (–Z)
            4,5,6,   4,6,7,

            // LEFT (–X)
            8,9,10,  8,10,11,

            // RIGHT (+X)
            12,13,14, 12,14,15,

            // TOP (+Y)
            16,17,18, 16,18,19,

            // BOTTOM (–Y)
            20,21,22, 20,22,23,
        };


        var uvArray = new Vector2[24];
        for (int i = 0; i < 6; i++)
        {
            uvArray[i * 4] = new Vector2(0, 0);
            uvArray[i * 4 + 1] = new Vector2(1, 0);
            uvArray[i * 4 + 2] = new Vector2(1, 1);
            uvArray[i * 4 + 3] = new Vector2(0, 1);
        }
        cubeMesh = new Mesh();
        cubeMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
        //Unity defaults to UInt16, which supports only 65535 vertices —
        // but you have only 24, so this one doesn’t break you.
        // Still good practice.
        cubeMesh.vertices = vertArray;
        cubeMesh.triangles = triangleArray;
        cubeMesh.uv = uvArray;
        cubeMesh.normals = normalArray;
        cubeMesh.RecalculateBounds();
    }
    void OnDestroy()
    {
        aliveBuffer?.Release();
        aliveCounterBuffer?.Release();
        accumBuffer?.Release();
        aliveArgsBuffer?.Release();
        deadArgsBuffer?.Release();
        deadBuffer?.Release();
        deadCounterBuffer?.Release();
        Destroy(_material01);
        Destroy(_material02);
    }
}
