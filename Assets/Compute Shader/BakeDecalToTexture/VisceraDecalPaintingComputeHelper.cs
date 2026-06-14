using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

public class VisceraDecalPaintingComputeHelper : MonoBehaviour
{
    [StructLayout(LayoutKind.Sequential)]
    public struct AtlasData
    {
        public int atlasIndex;
        public Vector3 padding;
        public Vector2 uvOffset;
        public Vector2 uvSize;
        public uint valid;
    }
    private ComputeBuffer atlasDataBuffer;

    [StructLayout(LayoutKind.Sequential)]
    public struct AccumData
    {
        public uint colorR;
        public uint colorG;
        public uint colorB;
        public uint colorA;

        public uint weight;
        public uint lastWriteFrame;
        public uint pad0;
        public uint pad1;
    }
    private ComputeBuffer accumDataBuffer;
    private ComputeBuffer dripEdgesBuffer;
    private ComputeBuffer dripReadBuffer, dripWriteBuffer;

    [Header("Compute")]
    [SerializeField] private ComputeShader computeShader;
    private VisceraTextureSO persistantAtlasSO;
    private Texture2D persistantAtlas;
    [SerializeField] private RenderTexture runtimeAtlasRT;

    [Header("Debug")]
    public bool _bakeDecal = false;
    [SerializeField] private bool _clearDecal = false;
    [SerializeField] private Material debugMaterial;

    private RenderTexture inputUvRT;
    private RenderTexture inputDecal;
    private RenderTexture inputDDX;
    private RenderTexture inputWNormal;
    private RenderTexture inputWPosDDX;
    private RenderTexture inputWPosDDY;

    private int accumKernel, clearAccumKernel, edgeDetectKernel, dripFlowkernel, bakeKernel;
    private bool isRunning;
    private VisceraTextureSO.AtlasData[] atlasData;
    private static int GLOBAL_MASK_PROP = Shader.PropertyToID("_VisceraRuntimeBakedMask");
    int atlasTexelCount;
    private void Start()
    {
        Init();
        InitShader();
        InitBuffers();
    }
    private void Init()
    {
        persistantAtlasSO = Resources.Load<VisceraTextureSO>("VisceraDecalBaker/VisceraTextureSO");
        persistantAtlas = Resources.Load<Texture2D>("VisceraDecalBaker/MaskAtlas_DiffuseAtlas");
        runtimeAtlasRT = ConvertTexture2DToRT(persistantAtlas);
        Shader.SetGlobalTexture(GLOBAL_MASK_PROP, runtimeAtlasRT);
    }
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.B))
        {
            _bakeDecal = true;
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            _clearDecal = true;
        }
    }
    private void LateUpdate()
    {
        if (!persistantAtlasSO || !persistantAtlas)
            return;

        if (!BakeDecalOpaquesToTextureMRT.MRTRenderTextureProvider.hasRenderedRT ||
            !BakeDecalToTextureMRT.MRTRenderTextureProvider.hasRenderedRT)
            return;

        inputUvRT = BakeDecalOpaquesToTextureMRT.MRTRenderTextureProvider.uvRTCapture;
        inputDDX = BakeDecalOpaquesToTextureMRT.MRTRenderTextureProvider.ddxyRTCapture;
        inputWNormal = BakeDecalOpaquesToTextureMRT.MRTRenderTextureProvider.wNormRTCapture;
        inputWPosDDX = BakeDecalOpaquesToTextureMRT.MRTRenderTextureProvider.wPosDDXRTCampture;
        inputWPosDDY = BakeDecalOpaquesToTextureMRT.MRTRenderTextureProvider.wPosDDYRTCampture;
        inputDecal = BakeDecalToTextureMRT.MRTRenderTextureProvider.decalRTCapture;

        SetShaderProperties();
        
        if (_bakeDecal)
        {
            DispatchAccumKernel();
            DispatchEdgeKernel(); 
            _bakeDecal = false;
        }
        else if (_clearDecal)
        {
            DispatchClearAccumKernel();
            _clearDecal = false;
        }

        DispatchDripFlowKernel();
        DispatchBakeKernel();
    }

    void SetShaderProperties()
    {
        computeShader.SetInt("_AtlasIndexCount", atlasData.Length);
        computeShader.SetVector("_InputTexSize", new Vector2(inputUvRT.width, inputUvRT.height));
        computeShader.SetVector("_AtlasTexSize", new Vector2(runtimeAtlasRT.width, runtimeAtlasRT.height));
        computeShader.SetFloat("_DeltaTime", Time.deltaTime);
        computeShader.SetInt("_FrameIndex", Time.frameCount);
        computeShader.SetVector("_WorldGravity",new Vector3(0,-1,0));
        computeShader.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);
    }
    private void OnDestroy()
    {
        ReleaseBuffers();
    }

    private void InitShader()
    {
        accumKernel = computeShader.FindKernel("AccumKernel");
        clearAccumKernel = computeShader.FindKernel("ClearAccumKernel");
        bakeKernel = computeShader.FindKernel("BakeKernel");
        dripFlowkernel = computeShader.FindKernel("DripFlowKernel");
        edgeDetectKernel = computeShader.FindKernel("EdgeDetectKernel");
    }

    private void InitBuffers()
    {
        atlasData = persistantAtlasSO.FetchAllAtlasData();

        atlasDataBuffer = new ComputeBuffer(atlasData.Length, Marshal.SizeOf<AtlasData>(), ComputeBufferType.Structured);
        atlasDataBuffer.SetData(atlasData);

        atlasTexelCount = runtimeAtlasRT.width * runtimeAtlasRT.height;
        accumDataBuffer = new ComputeBuffer(atlasTexelCount, Marshal.SizeOf<AccumData>(), ComputeBufferType.Structured);
        accumDataBuffer.SetData(new AccumData[atlasTexelCount]);

        dripEdgesBuffer = new ComputeBuffer(atlasTexelCount, sizeof(uint), ComputeBufferType.Structured);
        dripEdgesBuffer.SetData(new uint[atlasTexelCount]);

        dripReadBuffer = new ComputeBuffer(atlasTexelCount, sizeof(uint), ComputeBufferType.Structured);
        dripReadBuffer.SetData(new uint[atlasTexelCount]);
        dripWriteBuffer = new ComputeBuffer(atlasTexelCount, sizeof(uint), ComputeBufferType.Structured);
        dripWriteBuffer.SetData(new uint[atlasTexelCount]);
    }

    private void ReleaseBuffers()
    {
        atlasDataBuffer?.Release();
        runtimeAtlasRT?.Release();
        accumDataBuffer?.Release();
        dripEdgesBuffer?.Dispose();
        dripReadBuffer?.Dispose();
        dripWriteBuffer?.Dispose();
    }
    private void DispatchAccumKernel()
    {
        if (!runtimeAtlasRT) return;

        computeShader.SetBuffer(accumKernel, "_AtlasDataBuffer", atlasDataBuffer);
        computeShader.SetBuffer(accumKernel, "_AccumDataBuffer", accumDataBuffer);
        computeShader.SetTexture(accumKernel, "_InputDDXRT", inputDDX);
        computeShader.SetTexture(accumKernel, "_InputUvRT", inputUvRT);

        computeShader.SetTexture(accumKernel, "_InputDecalRT", inputDecal);

        computeShader.GetKernelThreadGroupSizes(accumKernel, out uint tgx, out uint tgy, out _);
        int groupsX = Mathf.CeilToInt(inputUvRT.width / (float)tgx);
        int groupsY = Mathf.CeilToInt(inputUvRT.height / (float)tgy);

        computeShader.Dispatch(accumKernel, groupsX, groupsY, 1);
    }
    void SwapDripBuffers()
    {
        (dripReadBuffer, dripWriteBuffer) = (dripWriteBuffer, dripReadBuffer);
    }

    private void DispatchEdgeKernel()
    {
        if (!runtimeAtlasRT) return;

        computeShader.SetBuffer(edgeDetectKernel, "_AccumDataBuffer", accumDataBuffer);
        computeShader.SetBuffer(edgeDetectKernel, "_DripEdgesBuffer", dripEdgesBuffer);
        computeShader.SetTexture(edgeDetectKernel, "_InputWNormal", inputWNormal);
        computeShader.SetTexture(edgeDetectKernel, "_InputWPosDDX", inputWPosDDX);
        computeShader.SetTexture(edgeDetectKernel, "_InputWPosDDY", inputWPosDDY);

        computeShader.GetKernelThreadGroupSizes(edgeDetectKernel, out uint tgx, out uint tgy, out _);
        int groupsX = Mathf.CeilToInt(runtimeAtlasRT.width / (float)tgx);
        int groupsY = Mathf.CeilToInt(runtimeAtlasRT.height / (float)tgy);

        computeShader.Dispatch(edgeDetectKernel, groupsX, groupsY, 1);
    }
    private void DispatchDripFlowKernel()
    {
        if (!runtimeAtlasRT) return;

        computeShader.SetBuffer(dripFlowkernel, "_DripEdgesBuffer", dripEdgesBuffer);
        computeShader.SetBuffer(dripFlowkernel, "_DripRead", dripReadBuffer);
        computeShader.SetBuffer(dripFlowkernel, "_DripWrite", dripWriteBuffer);
        computeShader.SetTexture(dripFlowkernel, "_InputWNormal", inputWNormal);
        computeShader.SetTexture(dripFlowkernel, "_InputWPosDDX", inputWPosDDX);
        computeShader.SetTexture(dripFlowkernel, "_InputWPosDDY", inputWPosDDY);
        computeShader.SetTexture(dripFlowkernel, "_InputDDXRT", inputDDX);

        computeShader.GetKernelThreadGroupSizes(dripFlowkernel, out uint tgx, out uint tgy, out _);
        int gx = Mathf.CeilToInt(runtimeAtlasRT.width / (float)tgx);
        int gy = Mathf.CeilToInt(runtimeAtlasRT.height / (float)tgy);

        computeShader.Dispatch(dripFlowkernel, gx, gy, 1);

        SwapDripBuffers();
    }

    private void DispatchBakeKernel()
    {
        if (!runtimeAtlasRT) return;

        // CRITICAL: BakeKernel must read from the buffer that WAS the 'Write' buffer 
        // but is now the 'Read' buffer after the swap.
        computeShader.SetBuffer(bakeKernel, "_DripRead", dripReadBuffer);
        computeShader.SetBuffer(bakeKernel, "_AccumDataBuffer", accumDataBuffer);
        computeShader.SetTexture(bakeKernel, "_OutputAtlasRT", runtimeAtlasRT);
        computeShader.SetTexture(bakeKernel, "_InputWNormal", inputWNormal);
        computeShader.SetTexture(bakeKernel, "_InputWPosDDX", inputWPosDDX);
        computeShader.SetTexture(bakeKernel, "_InputWPosDDY", inputWPosDDY);

        computeShader.GetKernelThreadGroupSizes(bakeKernel, out uint tgx, out uint tgy, out _);
        int gx = Mathf.CeilToInt(runtimeAtlasRT.width / (float)tgx);
        int gy = Mathf.CeilToInt(runtimeAtlasRT.height / (float)tgy);

        computeShader.Dispatch(bakeKernel, gx, gy, 1);
    }

    private void DispatchClearAccumKernel()
    {
        if (!runtimeAtlasRT) return;

        computeShader.SetBuffer(clearAccumKernel, "_AtlasDataBuffer", atlasDataBuffer);
        computeShader.SetBuffer(clearAccumKernel, "_AccumDataBuffer", accumDataBuffer);
        computeShader.SetTexture(clearAccumKernel, "_InputDDXRT", inputDDX);
        computeShader.SetTexture(clearAccumKernel, "_InputUvRT", inputUvRT);

        computeShader.SetTexture(clearAccumKernel, "_InputDecalRT", inputDecal);

        computeShader.GetKernelThreadGroupSizes(clearAccumKernel, out uint tgx, out uint tgy, out _);
        int groupsX = Mathf.CeilToInt(inputUvRT.width / (float)tgx);
        int groupsY = Mathf.CeilToInt(inputUvRT.height / (float)tgy);

        computeShader.Dispatch(clearAccumKernel, groupsX, groupsY, 1);
    }
    public void FinalizeBake()
    {
        Texture2D result = ConvertRTToTexture2D(runtimeAtlasRT, true);
        SaveTextureRuntime(result, "BakedAtlas.png");
        Destroy(runtimeAtlasRT);
    }
    void SaveTextureRuntime(Texture2D tex, string fileName)
    {
        byte[] png = tex.EncodeToPNG();

        string path = Path.Combine(
            Application.persistentDataPath,
            fileName
        );

        File.WriteAllBytes(path, png);
        Debug.Log($"Saved baked texture to: {path}");
    }

    Texture2D ConvertRTToTexture2D(RenderTexture rt, bool sRGB)
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, !sRGB);

        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply(false);

        RenderTexture.active = prev;
        return tex;
    }
    RenderTexture ConvertTexture2DToRT(Texture2D source)
    {
        RenderTextureDescriptor desc = new RenderTextureDescriptor(source.width, source.height, RenderTextureFormat.ARGB32, 0)
        {
            enableRandomWrite = true,
            useMipMap = false,
            autoGenerateMips = false,
            sRGB = true
        };

        RenderTexture rt = new RenderTexture(desc);
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Clamp;
        rt.Create();

        Graphics.Blit(source, rt);

        return rt;
    }
}
