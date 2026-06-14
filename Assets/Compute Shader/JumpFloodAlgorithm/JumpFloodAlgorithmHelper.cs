using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class JumpFloodAlgorithmHelper : MonoBehaviour
{
    private ComputeShader _shader;
    private Material _material;
    private int _initSeedKernelID, _jfaKernelID,_computeSDFkernelID;
    private int _maxSteps;
    private Vector2Int groupSizeXY;
    private RenderTexture _backBuffer, _frontBuffer,_resultBuffer;
    [SerializeField] private Texture2D _tex;
    [SerializeField] private Vector2 _fallOff;
    [SerializeField, Range(0, 1)] private float _alphaCutOff = 0.01f;
    [SerializeField] private Color _color = Color.red;
    void Start()
    {
        Init();
    }
    void OnDisable()
    {
        _backBuffer?.Release();
        _frontBuffer?.Release();
        _resultBuffer?.Release();
    }
    void OnDestroy()
    {
        _backBuffer?.Release();
        _frontBuffer?.Release();
        _resultBuffer?.Release();
    } 
    private void Init(){
        if (!_shader)
        {
            string[] guids = AssetDatabase.FindAssets("JumpFloodAlgorithmCompute t:ComputeShader");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            }
        } 
        RenderTexture CreateRT(string name)
        {
            var rt = new RenderTexture(_tex.width, _tex.height, 0, RenderTextureFormat.ARGBFloat);
            rt.name = name;
            rt.filterMode = FilterMode.Bilinear;
            rt.enableRandomWrite = true;
            rt.Create();
            return rt;
        }
        _frontBuffer = CreateRT("FrontBufferRT");        
        _backBuffer = CreateRT("BackBufferRT");
        _resultBuffer = CreateRT("ResultRT");   
             
        _initSeedKernelID = _shader.FindKernel("InitSeeds");
        _jfaKernelID = _shader.FindKernel("StepJFA");
        _computeSDFkernelID = _shader.FindKernel("ComputeSDF");

        _shader.SetTexture(_initSeedKernelID, "_InputTex", _tex);
        uint tx, ty, _;
        _shader.GetKernelThreadGroupSizes(_initSeedKernelID, out tx, out ty, out _);
        groupSizeXY = new Vector2Int(
            Mathf.CeilToInt(_tex.width / (float)tx),
            Mathf.CeilToInt(_tex.height / (float)ty)
        );
        _material = GetComponent<MeshRenderer>().material;

        _maxSteps = Mathf.CeilToInt(Mathf.Log(Mathf.Max(_tex.width, _tex.height), 2));
        _shader.SetInt("_MaxSteps", _maxSteps);
        _shader.SetVector("_TexSize", new Vector2(_tex.width, _tex.height));
    }
    void Update()
    {                
        _shader.SetVector("_FallOff", _fallOff);
        _shader.SetFloat("_AlphaCutOff", _alphaCutOff);
        _shader.SetVector("_Color", new Vector4(_color.a, _color.g, _color.b, _color.a));
                
        _shader.SetTexture(_initSeedKernelID, "_FrontBuffer", _frontBuffer);      
        _shader.SetTexture(_initSeedKernelID, "_BackBuffer", _backBuffer);

        _shader.Dispatch(_initSeedKernelID, groupSizeXY.x, groupSizeXY.y, 1);                
    
        for (int i = 0; i < _maxSteps; i++)
        {
            bool even = (i % 2 == 0);
            var src = even ? _frontBuffer : _backBuffer;
            var dst = even ? _backBuffer : _frontBuffer;

            //1 << 3 = 1 * 2^3 = 8
            int stepWidth = 1 << (_maxSteps - i - 1);//1 << x =>1 * (2^x) (for int)
            _shader.SetFloat("_StepWidth", stepWidth);
            _shader.SetTexture(_jfaKernelID, "_FrontBuffer", src);
            _shader.SetTexture(_jfaKernelID, "_BackBuffer", dst);
            _shader.Dispatch(_jfaKernelID, groupSizeXY.x, groupSizeXY.y, 1);
        }

        var latest = (_maxSteps % 2 == 0) ? _frontBuffer : _backBuffer;
        _shader.SetTexture(_computeSDFkernelID, "_FrontBuffer", latest);
        _shader.SetTexture(_computeSDFkernelID, "_Result", _resultBuffer);
        _shader.Dispatch(_computeSDFkernelID, groupSizeXY.x, groupSizeXY.y, 1);

        // var final = (_maxSteps % 2 == 0) ? _frontBuffer : _backBuffer ;
        // // Graphics.Blit(_backBuffer, final);
        _material.SetTexture("_MainTex", _resultBuffer);
    }
}
