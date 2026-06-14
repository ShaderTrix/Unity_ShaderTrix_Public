using System.Collections.Generic;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;

[ExecuteAlways]
public class BVHHelper : MonoBehaviour
{
    private enum ReadOutput
    {
        ScriptableObject,
        BinFile,
    }    
    [SerializeField] private ReadOutput _readOutput;
    [Header("ScriptableObject Mode")]
    public BVHAsset _bvhAsset;

    [Header("Bin File Mode")]
    public string binFileName;

    [Header("Settings")]
    [SerializeField] private int _gizmoDepth;
    [SerializeField] private bool _gizmoOnlyLeafNode;

    public ComputeBuffer _bvhBuffer, _triangleBuffer;
    private BVHAsset _runtimeAsset;
    public TMP_Text _bvhDebugText;
    private int _cachedBvhSize;
    private int _cachedTriSize;

    private void OnValidate()
    {
    #if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                    Rebuild();
            };
    #endif
    }


    private void OnEnable()
    {
        Rebuild();
    }

    private void OnDisable()
    {
        Cleanup();
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    private void Rebuild()
    {
        Cleanup(); 
        switch (_readOutput)
        {
            case ReadOutput.ScriptableObject:
                if (_bvhAsset != null)
                {
                    (_bvhBuffer, _triangleBuffer) = _bvhAsset.CreateBuffers();
                }
                break;

            case ReadOutput.BinFile:
                if (!string.IsNullOrEmpty(binFileName))
                {
                    var path = System.IO.Path.Combine(Application.streamingAssetsPath, binFileName);
                    if (System.IO.File.Exists(path))
                    {
                        Debug.Log($"[BVHHelper] Reading file from {path}");
                        BVHStructure.BvhData[] nodes;
                        BVHStructure.Triangle[] tris;
                        BVHAsset.LoadSceneFromBin(path, out nodes, out tris);

                        _runtimeAsset = ScriptableObject.CreateInstance<BVHAsset>();
                        _runtimeAsset._bvhDataList = new List<BVHStructure.BvhData>(nodes);
                        _runtimeAsset._triangleList = new List<BVHStructure.Triangle>(tris);

                        (_bvhBuffer, _triangleBuffer) = _runtimeAsset.CreateBuffers();
                        
                    }
                    else
                    {
                        Debug.LogWarning($"[BVHHelper] Bin file not found: {path}");
                    }
                }
                break;
        }
        // _cachedBvhSize = _bvhBuffer.count * _bvhBuffer.stride;
        // _cachedTriSize = _triangleBuffer.count * _triangleBuffer.stride;
        
        // _bvhDebugText.text =
        //     $"Bounding Volume Hierarchy Stats:\n" +
        //     $"Tree Depth : {_gizmoDepth}\n" +
        //     $"BVH Buffer size = {_cachedBvhSize} bytes\n" +
        //     $"BVH Triangle Buffer size = {_cachedTriSize} bytes";
    }

    private void Cleanup()
    {
        if (_bvhBuffer != null)
        {
            _bvhBuffer.Release();
            _bvhBuffer = null;
        }

        if (_triangleBuffer != null)
        {
            _triangleBuffer.Release();
            _triangleBuffer = null;
        }

        if (_runtimeAsset != null)
        {
            DestroyImmediate(_runtimeAsset);
            _runtimeAsset = null;
        }
    }

    private void OnDrawGizmosSelected()
    {
        var assetToDraw = _bvhAsset != null ? _bvhAsset : _runtimeAsset;
        if (assetToDraw != null)
            assetToDraw.DrawGizmo(_gizmoDepth, _gizmoOnlyLeafNode);
    }
}
