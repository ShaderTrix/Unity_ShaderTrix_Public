using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[CreateAssetMenu(menuName = "CustomScriptableObjects/Atlas Registry")]
public class VisceraTextureSO : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public int atlasIndex;
        public Vector2 uvOffset;
        public Vector2 uvSize;
    }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct AtlasData
    {
        public int atlasIndex;
        public Vector3 padding;   
        public Vector2 uvOffset;
        public Vector2 uvSize;
        public uint valid; 

        public AtlasData(int index, Vector2 offset, Vector2 size)
        {
            atlasIndex = index;
            padding = Vector3.zero;
            uvOffset = offset;
            uvSize = size;
            valid = 0;
        }
    }

    public Texture2D atlasTexture;
    public List<Entry> entries = new List<Entry>();

    public int AtlasCount => entries.Count;
    public AtlasData FetchAtlasData(int atlasIndex)
    {
        var entry = entries.FirstOrDefault(e => e.atlasIndex == atlasIndex);

        if (entry == null)
        {
            Debug.LogError($"Atlas index {atlasIndex} not found.");
            return default;
        }

        return new AtlasData(
            entry.atlasIndex,
            entry.uvOffset,
            entry.uvSize
        );
    }
    public AtlasData[] FetchAllAtlasData()
    {
        return entries
            .OrderBy(e => e.atlasIndex) // IMPORTANT
            .Select(e => new AtlasData(
                e.atlasIndex,
                e.uvOffset,
                e.uvSize
            ))
            .ToArray();
    }
}
