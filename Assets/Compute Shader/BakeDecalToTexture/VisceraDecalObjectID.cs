using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public class VisceraDecalObjectID : MonoBehaviour
{
    public VisceraTextureSO so;

    [System.Serializable]
    public class MaterialAtlasData
    {
        public int atlasIndex;
        public Vector2 maskUVSize;
        public Vector2 maskUVOffset;
    }

    [SerializeField]
    private List<MaterialAtlasData> materialData = new List<MaterialAtlasData>();

    static readonly int AtlasIndexProp = Shader.PropertyToID("_AtlasIndex");
    static readonly int uvSizeProp = Shader.PropertyToID("_UVSize");
    static readonly int uvOffsetProp = Shader.PropertyToID("_UVOffset");

    Renderer _renderer;
    MaterialPropertyBlock[] mpbs;

    void Awake()
    {
        _renderer = GetComponent<Renderer>();
        if (_renderer == null)
        {
            Debug.LogError("VisceraDecalObjectID requires a Renderer.");
            return;
        }

        int matCount = _renderer.sharedMaterials.Length;
        mpbs = new MaterialPropertyBlock[matCount];

        for (int i = 0; i < matCount; i++)
            mpbs[i] = new MaterialPropertyBlock();

        if (!Application.isPlaying)
            return;

        FetchUVAdjustments();
        ApplyRuntime();
    }

    /// <summary>
    /// Called by the atlas generator (EDITOR TIME)
    /// </summary>
    public void SetMaterialIndices(List<int> atlasIndices)
    {
        materialData.Clear();

        foreach (int index in atlasIndices)
        {
            materialData.Add(new MaterialAtlasData
            {
                atlasIndex = index
            });
        }

        if (!Application.isPlaying)
            return;

        FetchUVAdjustments();
        ApplyRuntime();
    }

    void FetchUVAdjustments()
    {
        if (so == null || so.entries == null)
        {
            Debug.LogError("VisceraTextureSO or entries missing.");
            return;
        }

        for (int i = 0; i < materialData.Count; i++)
        {
            var entry = so.entries.SingleOrDefault(e => e.atlasIndex == materialData[i].atlasIndex);
            if (entry == null)
            {
                Debug.LogError($"No atlas entry found for index {materialData[i].atlasIndex}");
                continue;
            }

            materialData[i].maskUVSize = entry.uvSize;
            materialData[i].maskUVOffset = entry.uvOffset;
        }
    }

    void ApplyRuntime()
    {
        int atlasCount = so.entries.Count;

        for (int i = 0; i < materialData.Count && i < mpbs.Length; i++)
        {
            float normalizedID = (materialData[i].atlasIndex + 0.5f) / atlasCount;

            var mpb = mpbs[i];
            mpb.Clear();

            mpb.SetFloat(AtlasIndexProp, normalizedID);
            mpb.SetVector(uvSizeProp, materialData[i].maskUVSize);
            mpb.SetVector(uvOffsetProp, materialData[i].maskUVOffset);

            _renderer.SetPropertyBlock(mpb, i);
        }
    }
}
