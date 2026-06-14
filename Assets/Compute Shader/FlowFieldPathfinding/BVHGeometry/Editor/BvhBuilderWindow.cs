using System.IO;
using UnityEditor;
using UnityEngine;
using System;

public class BvhBuilderWindow : EditorWindow
{
    private enum WriteOutput
    {
        ScriptableObject,
        BinFile,
    }
    public GameObject meshObjectRoot;
    public int splitCount = 32;
    private WriteOutput outputMode;
    string lastPath;

    [MenuItem("Window/BvhBuilder")]
    static void Init()
    {
        var window = GetWindowWithRect<BvhBuilderWindow>(new Rect(0f, 0f, 400f, 130f));
        window.Show();
    }

    void OnGUI()
    {
        GUILayout.Space(32f);

        meshObjectRoot = EditorGUILayout.ObjectField(nameof(meshObjectRoot), meshObjectRoot, typeof(GameObject), true) as GameObject;
        splitCount = EditorGUILayout.IntField(nameof(splitCount), splitCount);
        outputMode = (WriteOutput)EditorGUILayout.EnumPopup("Output Mode", outputMode);

        GUI.enabled = (meshObjectRoot != null) && (splitCount > 0);
        if (GUILayout.Button("Build"))
        {
            var directory = "Assets";
            var defaultName = "bvhAsset";
            if (!string.IsNullOrEmpty(lastPath))
            {
                directory = Path.GetDirectoryName(lastPath);
                defaultName = Path.GetFileName(lastPath);
            }

            string extension = outputMode == WriteOutput.BinFile ? "bin" : "asset";
            string title = outputMode == WriteOutput.BinFile ? "Save BVH bin" : "Save BVH asset";
            var path = EditorUtility.SaveFilePanel(title, directory, defaultName, extension);
            if (!string.IsNullOrEmpty(path))
            {
                lastPath = path;
                var (bvhDatas, triangles) = BVHEngine.BuildBvh(meshObjectRoot, splitCount);

                switch (outputMode)
                {
                    case WriteOutput.ScriptableObject:
                        path = Path.ChangeExtension(path, "asset");
                        var relativePath = "Assets" + path.Substring(Application.dataPath.Length);
                        var bvhAsset = AssetDatabase.LoadAssetAtPath<BVHAsset>(relativePath);
                        if (bvhAsset == null)
                        {
                            bvhAsset = CreateInstance<BVHAsset>();
                            AssetDatabase.CreateAsset(bvhAsset, relativePath);
                        }
                        bvhAsset._bvhDataList = bvhDatas;
                        bvhAsset._triangleList = triangles;

                        EditorUtility.SetDirty(bvhAsset);
                        AssetDatabase.SaveAssets();
                        EditorGUIUtility.PingObject(bvhAsset);
                        break;

                    case WriteOutput.BinFile:
                        var binPath = Path.ChangeExtension(path, "bin");
                        Directory.CreateDirectory(Path.GetDirectoryName(binPath));
                        BVHAsset.SaveSceneToBin(binPath, bvhDatas.ToArray(), triangles.ToArray());
                        AssetDatabase.Refresh();
                        Debug.Log($"BVH bin written: {binPath}");
                        break;
                }
            }
        }
    }
}

