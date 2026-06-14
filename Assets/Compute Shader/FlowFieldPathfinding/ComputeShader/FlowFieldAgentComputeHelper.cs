using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;
using UnityEditor;
using System.Linq;
using Unity.Collections;

public class FlowFieldAgentComputeHelper : MonoBehaviour
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Agents
    {
        //16 bytes
        public Vector3 position;
        public float speed;
        //16 bytes
        public Vector3 direction;
        public float noiseOffset;
        //16 bytes
        public Vector3 idleOrigin;   // where idle movement started
        // public float alive;
        public float frame;
        //16 bytes
        public Vector3 idleDir;
        public float idleReturnDist;
        //16 bytes
        public float prevState;
        public float slidingTime;
        public float gravity;
        public float texRandom;
        public Agents(Vector3 pos, Vector3 dir, float spd)
        {
            position = pos;
            direction = dir;
            speed = spd;
            // alive = 1;
            noiseOffset = Random.value * 1000;
            idleOrigin = pos;
            idleDir = new Vector3(Random.insideUnitCircle.x, 0f, Random.insideUnitCircle.y);
            idleReturnDist = Random.Range(0.75f, 1.5f);
            prevState = 0;
            frame = slidingTime = gravity = 0.0f;
            texRandom = (uint)Random.Range(0, 4);
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct FlowCell
    {
        public Vector2 bestDir;
        public FlowCell(Vector2 d) { bestDir = d; }
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct CustomEffects
    {
        public Vector3 repelPositions;
        public float repelRadius;
        public Vector3 spiralPositions;
        public float spiralRadius;
    };
    [SerializeField] private FlowField_GridController _gridController;
    [SerializeField] private FlowField _flowField;
    [SerializeField] private BVHHelper _bvhData;
    private ComputeShader _computeShader;
    [SerializeField] private Material _material;
    [SerializeField] private Transform[] _spawnLocations;
    [SerializeField] private Transform[] _repelLocations, _spiralLocations;

    [Header("Agent Settings")]
    [SerializeField] private SkinnedMeshRenderer _agentMesh;
    [SerializeField] private int _agentsCount = 500;
    [SerializeField] private float _spawnRadius = 3.0f;
    [SerializeField] private float _agentSpeed = 2.0f;
    [SerializeField] private float _agentSize = 1.0f;
    [SerializeField] private float _agentSpeedVariation = 1.0f;
    [SerializeField] private float _neigbourDist = 1.0f;
    private List<FlowCell> cells = new();

    private int numOfAgents;
    private List<ComputeBuffer> _argsBuffers = new();
    public ComputeBuffer _agentsBuffer { get; private set; }
    private ComputeBuffer _flowCellsBuffer, _customEffectsBuffer;
    private readonly static int AGENTBUFFER_STRIDE = Marshal.SizeOf(typeof(Agents));
    private readonly static int ARGSBUFFER_STRIDE = 5 * sizeof(uint);
    private readonly static int FLOWCELL_STRIDE = Marshal.SizeOf(typeof(FlowCell));
    private readonly static int CUSTOMEFFECTS_STRIDE = Marshal.SizeOf(typeof(CustomEffects));
    private MaterialPropertyBlock props;
    // private uint[][] args;// = new uint[5] { 0, 0, 0, 0, 0 };
    private Agents[] agentsArray;
    private int groupSizeX, kernelID;
    private Bounds _bounds;
    private Vector2Int _gridSize;
    private float _cellSize;
    private Vector3 _gridOrigin;
    private IndirectRendererHelper _lodRenderer { get => GetComponent<IndirectRendererHelper>(); }
    public static class ComputeShaderParams
    {
        public static int ParticleBuffer = Shader.PropertyToID("");
    }
    private void Start()
    {
        _flowField = new FlowField(_gridController._cellRadius, _gridController._gridSize);
        if (!_computeShader)
        {
            string[] guids = AssetDatabase.FindAssets("FlowFieldAgentCompute t:ComputeShader");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            }
        }
        Init();
        InitAgents();
        // InitAnimationBuffers();
        BuildArgsAndBuffers();
        BakeAndUploadFlowField();
        InitBVHData();
    }

    private void Init()
    {
        kernelID = _computeShader.FindKernel("CSMain");
        _computeShader.GetKernelThreadGroupSizes(kernelID, out uint threadGroupSizeX, out uint threadGroupSizeY, out uint threadGroupSizeZ);
        groupSizeX = Mathf.CeilToInt((float)_agentsCount / (float)threadGroupSizeX);
        numOfAgents = Mathf.CeilToInt((float)_agentsCount);

        _bounds = new Bounds(Vector3.zero, Vector3.one * 1000);

        props = new MaterialPropertyBlock();
        props.SetFloat("_UniqueID", Random.value);

    }
    private void InitAgents()
    {
        agentsArray = new Agents[numOfAgents];
        for (int i = 0; i < numOfAgents; i++)
        {
            var spawn = _spawnLocations.Length > 0 ? _spawnLocations[Random.Range(0, _spawnLocations.Length)] : this.transform;
            Vector2 randomCircle = Random.insideUnitCircle * _spawnRadius;
            Vector3 pos = spawn.position + new Vector3(randomCircle.x, 0.1f, randomCircle.y);
            var dir = spawn.forward;
            var speed = _agentSpeed;
            agentsArray[i] = new Agents(pos, dir, speed);
        }
        for (int i=0;i<Mathf.Min(8, agentsArray.Length); i++)
        Debug.Log($"###Agent[{i}] pos={agentsArray[i].position}");
    }
    private void InitBVHData()
    {
        _computeShader.SetBuffer(kernelID, "_BvhDataBuffer", _bvhData._bvhBuffer);
        _computeShader.SetBuffer(kernelID, "_TriangleBuffer", _bvhData._triangleBuffer);
    }
    private void UpdateEffectModifiers()
    {
        if ((_repelLocations == null || _repelLocations.Length == 0) &&
            (_spiralLocations == null || _spiralLocations.Length == 0))
            return;

        var effects = new List<CustomEffects>();
        if (_repelLocations != null)
        {
            foreach (var repel in _repelLocations)
            {
                if (repel == null) continue;

                effects.Add(new CustomEffects
                {
                    repelPositions = repel.position,
                    repelRadius = (repel.localScale.x + repel.localScale.y + repel.localScale.z) / 3f,
                    spiralPositions = Vector3.zero,
                    spiralRadius = 0f
                });
            }
        }
        if (_spiralLocations != null)
        {
            foreach (var spiral in _spiralLocations)
            {
                if (spiral == null) continue;

                effects.Add(new CustomEffects
                {
                    repelPositions = Vector3.zero,
                    repelRadius = 0f,
                    spiralPositions = spiral.position,
                    spiralRadius = (spiral.localScale.x + spiral.localScale.y + spiral.localScale.z) / 3f
                });
            }
        }
        if (_customEffectsBuffer == null || _customEffectsBuffer.count != effects.Count)
        {
            _customEffectsBuffer?.Dispose();
            _customEffectsBuffer = new ComputeBuffer(effects.Count, CUSTOMEFFECTS_STRIDE, ComputeBufferType.Structured);
        }

        _customEffectsBuffer.SetData(effects);
        _computeShader.SetBuffer(kernelID, "_CustomEffectsBuffer", _customEffectsBuffer);
        _computeShader.SetInt("_RepelCount", _repelLocations?.Length ?? 0);
        _computeShader.SetInt("_SpiralCount", _spiralLocations?.Length ?? 0);
    }

    // private void InitAnimationBuffers()
    // {
    //     if (!Directory.Exists(_animationFilePath))
    //     {
    //         Debug.LogError($"Animation folder not found: {_animationFilePath}");
    //         return;
    //     }

    //     var files = Directory.EnumerateFiles(_animationFilePath, "*.bytes")
    //                          .OrderBy(f => f);

    //     List<VertexAnimations> allAnimData = new();

    //     int totalFrames = 0;
    //     int vertexCount = 0;
    //     List<float> offsets = new();

    //     foreach (var file in files)
    //     {
    //         var (vertices, normals) = VertexAnimationBinaryBaker.LoadFromFile(
    //             file, out int fileFrames, out int fileVertexCount);

    //         if (vertexCount == 0)
    //         {
    //             vertexCount = fileVertexCount;
    //         }
    //         else if (vertexCount != fileVertexCount)
    //         {
    //             Debug.LogWarning($"File {file} has a different vertex count ({fileVertexCount}). Expected {vertexCount}.");
    //         }
    //         offsets.Add(totalFrames);

    //         var animData = vertices
    //             .SelectMany((frameVerts, frameIndex) =>
    //                 frameVerts.Select((v, vertIndex) =>
    //                     new VertexAnimations(v, normals[frameIndex][vertIndex])
    //                 )
    //             );

    //         allAnimData.AddRange(animData);

    //         totalFrames += fileFrames;
    //         Debug.Log($"Appended {fileFrames} frames from {Path.GetFileName(file)}");
    //     }

    //     if (allAnimData.Count == 0)
    //     {
    //         Debug.LogWarning("No animation data found.");
    //         return;
    //     }

    //     _vertexAnimationBuffer = new ComputeBuffer(
    //         allAnimData.Count,
    //         VERTEXANIMBUFFER_STRIDE,
    //         ComputeBufferType.Structured
    //     );
    //     _vertexAnimationBuffer.SetData(allAnimData);

    //     Debug.Log($"Loaded {totalFrames} total frames × {vertexCount} vertices per frame.");
    //     offsets.ToList().ForEach(x => Debug.Log(x));

    //     _computeShader.SetBuffer(kernelID, "_VertexAnimationBuffer", _vertexAnimationBuffer);
    //     _material.SetBuffer("_VertexAnimationBuffer", _vertexAnimationBuffer);

    //     _computeShader.SetInt("_NumOfFrames", totalFrames);
    //     _material.SetInteger("_NumOfFrames", totalFrames);
    //     _material.SetInteger("_TotalVertexCount", vertexCount);

    //     _computeShader.SetFloats("_AnimationOffsets", offsets.ToArray());
    //     _material.SetFloatArray("_AnimationOffsets", offsets.ToArray());
    // }

    private void BuildArgsAndBuffers()
    {
        // foreach (var buf in _argsBuffers) buf?.Dispose();
        // _argsBuffers.Clear();

        // var subMeshCount = _agentMesh.sharedMesh.subMeshCount;
        // for (int i = 0; i < subMeshCount; i++)
        // {
        //     var args = new uint[5];
        //     args[0] = _agentMesh.sharedMesh.GetIndexCount(i);
        //     args[1] = (uint)_agentsCount;
        //     args[2] = _agentMesh.sharedMesh.GetIndexStart(i);
        //     args[3] = _agentMesh.sharedMesh.GetBaseVertex(i);
        //     args[4] = 0;

        //     var argsBuffer = new ComputeBuffer(1, ARGSBUFFER_STRIDE, ComputeBufferType.IndirectArguments);
        //     argsBuffer.SetData(args);
        //     _argsBuffers.Add(argsBuffer);
        // }

        _agentsBuffer?.Release();
        _agentsBuffer = new ComputeBuffer(_agentsCount,AGENTBUFFER_STRIDE, ComputeBufferType.Structured);
        _agentsBuffer.SetData(agentsArray);

        _computeShader.SetBuffer(kernelID, "_AgentsBuffer", _agentsBuffer);
        _material.SetBuffer("_AgentsBuffer", _agentsBuffer);
        _material.enableInstancing = true;
    }
    private void Update()
    {
        if (_gridController._curFlowField == null) return;
        if (_gridController._curFlowField._destinationCell == null) return;

        UpdateEffectModifiers();

        var dest = _gridController._curFlowField._destinationCell._worldPos;
        _computeShader.SetVector("_Destination", dest);
        _computeShader.SetFloat("_DeltaTime", Time.deltaTime);
        _computeShader.SetFloat("_GlobalSpeedMul", 1.0f);
        _computeShader.SetInt("_AgentsCount", _agentsCount);
        _computeShader.SetFloat("_AgentSpeedVariation", _agentSpeedVariation);
        _computeShader.SetFloat("_AgentSpeed", _agentSpeed);

        _material.SetFloat("_AgentSize", _agentSize);
        _computeShader.Dispatch(kernelID, groupSizeX, 1, 1);

        // for (int i = 0; i < _agentMesh.sharedMesh.subMeshCount; i++)
        // {
        //     Graphics.DrawMeshInstancedIndirect(
        //         _agentMesh.sharedMesh, i, _material, _bounds, _argsBuffers[i], 0, props
        //     );
        // }        
        _lodRenderer.Render();
    }

    /// Call this whenever the CPU flow field is rebuilt or the destination changes.
    public void BakeAndUploadFlowField()
    {
        var flowField = _gridController._curFlowField;
        if (flowField == null) return;
        _gridSize = _gridController._gridSize;
        _cellSize = _gridController._cellRadius * 2f; //diameter

        // we are doing this to know the center of the grid,suppose we got a destination cell to go to now we want the center of the grid
        //(0,0) index cell is the bottom left in the 2D array..so basically for 32/32 gridSize we divide by 2 then we get middle most position as dot
        //but we need center most cell not single position...so we offset the position with cellSize + cellRadius to get a box instead of a point
        float originX = -(_gridSize.x / 2f) * _cellSize + _gridController._cellRadius;
        float originZ = -(_gridSize.y / 2f) * _cellSize + _gridController._cellRadius;
        _gridOrigin = new Vector3(originX, 0, originZ); // origin cells world position

        // Flatten cells into 1D array 
        if (cells == null)
            cells = new List<FlowCell>(_gridSize.x * _gridSize.y);
        else
        {
            cells.Clear();
            if (cells.Capacity < _gridSize.x * _gridSize.y)
                cells.Capacity = _gridSize.x * _gridSize.y;
        }
        for (int y = 0; y < _gridSize.y; y++)
        {
            for (int x = 0; x < _gridSize.x; x++)
            {
                var c = flowField.grid[x, y];
                Vector2 d = c._bestDirection._vector; // (x,y) from your grid (y = z on world)
                cells.Add(new FlowCell(new Vector2(d.x, d.y)));
            }
        }

        int requiredCount = cells.Count;
        if (_flowCellsBuffer == null || _flowCellsBuffer.count != requiredCount)
        {
            _flowCellsBuffer?.Dispose();
            _flowCellsBuffer = new ComputeBuffer(requiredCount, FLOWCELL_STRIDE, ComputeBufferType.Structured);
        }
        _flowCellsBuffer.SetData(cells);


        _computeShader.SetBuffer(kernelID, "_FlowCells", _flowCellsBuffer);
        _computeShader.SetInts("_GridSize", new int[] { _gridSize.x, _gridSize.y });
        _computeShader.SetFloat("_CellSize", _cellSize);
        _computeShader.SetVector("_GridOrigin", _gridOrigin);
        _computeShader.SetFloat("_CellRadius", flowField._cellRadius);
        _computeShader.SetFloat("_NeighbourDistance", _neigbourDist);
    }
    // public void UploadNativeFlowField(Vector2Int goal)
    // {
    //     var nativeCells = _flowField.FlowFieldJob(goal);
    //     BakeAndUploadNativeFlowField(nativeCells);
    // }

    // public void BakeAndUploadNativeFlowField(NativeArray<FlowFieldCellNative> native)
    // {
    //     if (_flowCellsBuffer == null || _flowCellsBuffer.count != native.Length)
    //     {
    //         _flowCellsBuffer?.Dispose();
    //         _flowCellsBuffer = new ComputeBuffer(native.Length, 
    //             Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<FlowFieldCellNative>());
    //     }

    //     _flowCellsBuffer.SetData(native);

    //     _computeShader.SetBuffer(kernelID, "_FlowFieldBuffer", _flowCellsBuffer);
    //     _computeShader.SetInts("_GridSize", _gridSize.x, _gridSize.y);
    //     _computeShader.SetFloat("_CellSize", _cellSize);
    //     _computeShader.SetVector("_GridOrigin", _gridOrigin);
    // }


    private void ReleaseBuffers()
    {
        _agentsBuffer?.Dispose();
        _agentsBuffer = null;

        _flowCellsBuffer?.Dispose();
        _flowCellsBuffer = null;

        foreach (var buf in _argsBuffers)
            buf?.Dispose();
        _argsBuffers.Clear();

        // _vertexAnimationBuffer?.Dispose();
        // _vertexAnimationBuffer = null;
    }

    private void OnDestroy() => ReleaseBuffers();
    private void OnDisable() => ReleaseBuffers();
}
