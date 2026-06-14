using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public class AtlasSource
{
    public Renderer renderer;
    public int materialIndex;
    public Material material;
    public Mesh mesh;
    public float normalizedID;
}

public class TextureData
{
    public Texture2D texture;
    public bool applyColor;
    public Color color;

    public TextureData(Texture2D tex, bool colorFlag, Color col)
    {
        texture = tex;
        applyColor = colorFlag;
        color = col;
    }
}

public class CombinedTextureData
{
    public Texture2D diffuseTexture;
    public Texture2D normalTexture;
    public bool applyColor;
    public Color color;
    public float normalizedID;

    public CombinedTextureData(Texture2D diffuseTex, Texture2D normalTex, bool colorFlag, Color col, float id)
    {
        diffuseTexture = diffuseTex;
        normalTexture = normalTex;
        applyColor = colorFlag;
        color = col;
        normalizedID = id;
    }
}

public class SimpleAtlasGenerator : EditorWindow
{
    // List to store selected GameObjects
    private ReorderableList reorderableList;
    private List<GameObject> selectedObjects = new List<GameObject>();

    // Settings
    private int selectedAtlasSizeIndex = 3; // Default to 2048
    private readonly int[] atlasSizes = new int[] { 256, 512, 1024, 2048, 4096 };

    private bool enableNormalMapAtlasing = false; // Toggle for normal map packing
    private int padding = 1;

    // Folder Structure
    private const string RESOURCES_ROOT = "Assets/Resources/VisceraDecalBaker";

    // Progress Indicator
    private bool isProcessing = false;

    // Warnings
    private List<string> uvWarnings = new List<string>();

    // Scroll position for the ReorderableList
    private Vector2 listScrollPos;

    // Preview-related
    private bool showPreview = false;                // If true, display preview in the EditorWindow
    private Texture2D previewDiffuseAtlas = null;    // Holds a generated diffuse atlas for preview
    private Texture2D previewNormalAtlas = null;     // Holds a generated normal atlas for preview

    // ========= Scroll for the entire window =========
    private Vector2 mainScrollPos;
    private enum RendererScope
    {
        OnlyOnSelectedObjects,
        SelectedObjectsAndChildren
    }
    private enum DiffuseAtlasMode
    {
        Color,
        Black
    }

    [SerializeField]
    private DiffuseAtlasMode diffuseAtlasMode = DiffuseAtlasMode.Color;

    [SerializeField]
    private RendererScope rendererScope = RendererScope.OnlyOnSelectedObjects;


    [MenuItem("Tools/Simple Atlas Generator")]
    public static void ShowWindow()
    {
        GetWindow<SimpleAtlasGenerator>("Simple Atlas Generator v0.1");
    }

    private void OnEnable()
    {
        // Initialize the ReorderableList
        reorderableList = new ReorderableList(selectedObjects, typeof(GameObject), true, true, true, true);

        reorderableList.drawHeaderCallback = (Rect rect) =>
        {
            EditorGUI.LabelField(rect, "Selected Objects");
        };

        reorderableList.drawElementCallback = DrawReorderableListElement;

        // Define the element height callback
        reorderableList.elementHeightCallback = (int index) =>
        {
            return EditorGUIUtility.singleLineHeight + 4; // Single row height
        };

        // Custom onAddCallback to add a null slot
        reorderableList.onAddCallback = (ReorderableList list) =>
        {
            selectedObjects.Add(null);
            Debug.Log("Simple Atlas Generator: Added a new slot. Drag a GameObject into the slot.");
        };

        reorderableList.onRemoveCallback = (ReorderableList list) =>
        {
            if (EditorUtility.DisplayDialog("Confirm Removal", "Are you sure you want to remove the selected object?", "Yes", "No"))
            {
                selectedObjects.RemoveAt(list.index);
                Debug.Log("Simple Atlas Generator: Removed selected object from the list.");
                Repaint();
            }
        };
    }

    private void DrawReorderableListElement(Rect rect, int index, bool isActive, bool isFocused)
    {
        if (index < selectedObjects.Count)
        {
            GameObject obj = selectedObjects[index];
            rect.y += 2;

            // Define column widths
            float objectColumnWidth = rect.width * 0.5f;
            float statsColumnWidth = rect.width * 0.45f; // Adjust as needed
            float spacing = rect.width * 0.05f;

            // Define positions
            Rect objectFieldRect = new Rect(rect.x, rect.y, objectColumnWidth, EditorGUIUtility.singleLineHeight);
            Rect pingButtonRect = new Rect(rect.x + objectColumnWidth + spacing, rect.y, 18, EditorGUIUtility.singleLineHeight);
            Rect statsRect = new Rect(rect.x + objectColumnWidth + spacing + 20, rect.y, statsColumnWidth - 20, EditorGUIUtility.singleLineHeight);

            // Display the GameObject field
            selectedObjects[index] = (GameObject)EditorGUI.ObjectField(
                objectFieldRect,
                obj,
                typeof(GameObject),
                true
            );

            // Add a button to ping the object in the hierarchy
            if (GUI.Button(pingButtonRect, "P"))
            {
                if (obj != null)
                {
                    Selection.activeGameObject = obj;
                    EditorGUIUtility.PingObject(obj);
                }
            }

            // Display stats beside the GameObject field
            if (obj != null)
            {
                LODGroup lodGroup = obj.GetComponent<LODGroup>();
                string lodInfo = lodGroup != null
                    ? $"LOD Levels: {lodGroup.GetLODs().Length}"
                    : "No LOD Group";

                string vertexInfo = "";
                MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    int vertexCount = meshFilter.sharedMesh.vertexCount;
                    vertexInfo = $"Vertices: {vertexCount}";
                }

                // Combine stats into a single line
                string combinedStats = lodInfo;
                if (!string.IsNullOrEmpty(vertexInfo))
                {
                    combinedStats += " | " + vertexInfo;
                }

                EditorGUI.LabelField(
                    statsRect,
                    combinedStats
                );
            }
        }
    }

    private void OnGUI()
    {
        // Begin a scroll view that wraps the entire window's content
        mainScrollPos = EditorGUILayout.BeginScrollView(mainScrollPos);
        rendererScope = (RendererScope)EditorGUILayout.EnumPopup(
            "Renderer Scope",
            rendererScope
        );

        GUILayout.Label("Simple Atlas Generator Settings", EditorStyles.boldLabel);
        diffuseAtlasMode = (DiffuseAtlasMode)EditorGUILayout.EnumPopup(
            "Diffuse Atlas Mode",
            diffuseAtlasMode
        );

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Selected Objects"))
        {
            AddSelectedObjects();
        }
        if (GUILayout.Button("Clear List"))
        {
            selectedObjects.Clear();
            Debug.Log("Simple Atlas Generator: Cleared selected objects list.");
            Repaint();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(5);

        // Limit the ReorderableList display to 10 items and add a scrollbar
        int maxVisibleItems = 10;
        float elementHeight = reorderableList.elementHeight;
        float headerHeight = reorderableList.headerHeight;
        float listHeight = headerHeight + elementHeight * maxVisibleItems;

        listScrollPos = EditorGUILayout.BeginScrollView(listScrollPos, GUILayout.Height(listHeight));
        reorderableList.DoLayoutList();
        EditorGUILayout.EndScrollView();

        GUILayout.Space(10);

        // Atlas Size Dropdown
        GUILayout.BeginHorizontal();
        GUILayout.Label("Max Atlas Size", GUILayout.Width(100));
        selectedAtlasSizeIndex = EditorGUILayout.Popup(
            selectedAtlasSizeIndex,
            atlasSizes.Select(size => size.ToString()).ToArray(),
            GUILayout.Width(100)
        );
        GUILayout.EndHorizontal();

        // Padding Input
        GUILayout.BeginHorizontal();
        GUILayout.Label("Padding (px)", GUILayout.Width(100));
        padding = EditorGUILayout.IntField(padding, GUILayout.Width(50));
        GUILayout.EndHorizontal();

        GUILayout.Space(10);

        // Buttons: Preview and Generate
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Preview Atlas") && !isProcessing)
        {
            // Clear old warnings and preview atlases
            uvWarnings.Clear();
            previewDiffuseAtlas = null;
            previewNormalAtlas = null;
            showPreview = false;

            isProcessing = true;
            PreviewAtlas(); // Only generate atlases for preview
            isProcessing = false;
        }

        if (GUILayout.Button("Generate Atlas") && !isProcessing)
        {
            // Clear old warnings, old previews
            uvWarnings.Clear();
            previewDiffuseAtlas = null;
            previewNormalAtlas = null;
            showPreview = false;

            if (selectedObjects.Count == 0 || selectedObjects.All(obj => obj == null))
            {
                EditorUtility.DisplayDialog("Simple Atlas Generator", "No valid objects selected.", "OK");
                Debug.LogWarning("Simple Atlas Generator: No valid GameObjects selected.");
                return;
            }

            isProcessing = true;
            GenerateAtlas();
            isProcessing = false;
        }
        GUILayout.EndHorizontal();

        // Show the preview if available
        if (showPreview && previewDiffuseAtlas != null)
        {
            GUILayout.Space(10);
            GUILayout.Label("Diffuse Atlas Preview:", EditorStyles.boldLabel);

            // Force the preview to a fixed 256x256 size
            Rect previewRect = GUILayoutUtility.GetRect(256, 256);
            EditorGUI.DrawPreviewTexture(previewRect, previewDiffuseAtlas, null, ScaleMode.ScaleToFit);

            if (previewNormalAtlas != null)
            {
                GUILayout.Label("Normal Atlas Preview:", EditorStyles.boldLabel);
                Rect previewRectNormal = GUILayoutUtility.GetRect(256, 256);
                EditorGUI.DrawPreviewTexture(previewRectNormal, previewNormalAtlas, null, ScaleMode.ScaleToFit);
            }
        }

        GUILayout.Space(10);

        // Display Warnings (including the note if UV is out of range)
        if (uvWarnings.Count > 0)
        {
            EditorGUILayout.HelpBox(
                "Some objects have UVs outside the 0-1 range.\n" +
                "The resulting atlas might look correct in preview, but final mapping could be problematic.",
                MessageType.Warning
            );
            foreach (var warning in uvWarnings)
            {
                EditorGUILayout.LabelField(warning);
            }
        }

        if (isProcessing)
        {
            GUILayout.Label("Processing...", EditorStyles.boldLabel);
        }

        // End the main scroll view
        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// Function to *only* generate the atlas textures (diffuse and normal) for a visual preview, 
    /// without modifying the scene meshes or materials.
    /// </summary>
    private void PreviewAtlas()
    {
        try
        {
            List<GameObject> validObjects = selectedObjects.Where(obj => obj != null).ToList();
            if (validObjects.Count == 0) return;

            List<Renderer> renderers = new List<Renderer>();
            foreach (var obj in validObjects)
            {
                if (rendererScope == RendererScope.OnlyOnSelectedObjects)
                {
                    var r = obj.GetComponent<Renderer>();
                    if (r != null) renderers.Add(r);
                }
                else
                {
                    renderers.AddRange(obj.GetComponentsInChildren<Renderer>(true));
                }
            }
            List<Texture2D> textures = new List<Texture2D>();

            foreach (var r in renderers)
            {
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) continue;

                    Texture2D tex = Texture2D.whiteTexture;
                    if (mat.HasProperty("_MainTex"))
                        tex = mat.GetTexture("_MainTex") as Texture2D ?? Texture2D.whiteTexture;

                    textures.Add(tex);
                }
            }

            if (renderers.Count == 0) return;

            int totalCount = textures.Count;
            int maxAtlasSize = atlasSizes[selectedAtlasSizeIndex];

            List<Vector2Int> textureSizes = new List<Vector2Int>();
            List<CombinedTextureData> dataList = new List<CombinedTextureData>();

            for (int i = 0; i < textures.Count; i++)
            {
                Texture2D tex = textures[i];

                int w = Mathf.Min(tex.width, maxAtlasSize - (padding * 2));
                int h = Mathf.Min(tex.height, maxAtlasSize - (padding * 2));

                float normalizedID = (i + 0.5f) / totalCount;

                textureSizes.Add(new Vector2Int(w, h));
                dataList.Add(new CombinedTextureData(tex, null, false, Color.white, normalizedID));
            }

            // 2. Pack
            var placements = TexturePacker.Pack(textureSizes, maxAtlasSize, padding);

            // 3. Generate Preview Texture
            previewDiffuseAtlas = new Texture2D(maxAtlasSize, maxAtlasSize, TextureFormat.RGBA32, false);
            Color[] clearColor = Enumerable.Repeat(new Color(0.1f, 0.1f, 0.1f, 1f), maxAtlasSize * maxAtlasSize).ToArray();
            previewDiffuseAtlas.SetPixels(clearColor);

            foreach (var kvp in placements)
            {
                int index = kvp.Key;
                Rect rect = kvp.Value;
                var data = dataList[index];

                if (diffuseAtlasMode == DiffuseAtlasMode.Black)
                {
                    Color[] idPixels = new Color[(int)rect.width * (int)rect.height];
                    Color idCol = new Color(0, 0, 0, data.normalizedID);
                    for (int p = 0; p < idPixels.Length; p++) idPixels[p] = idCol;
                    previewDiffuseAtlas.SetPixels((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height, idPixels);
                }
                else
                {
                    Texture2D resized = ResizeTexture(data.diffuseTexture, (int)rect.width, (int)rect.height, false);
                    previewDiffuseAtlas.SetPixels((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height, resized.GetPixels());
                }
            }

            previewDiffuseAtlas.Apply();
            showPreview = true;
            Debug.Log("Simple Atlas Generator: Preview generated with Bin Packing.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Preview Failed: {ex.Message}");
        }
    }

    private void AddSelectedObjects()
    {
        foreach (var obj in Selection.gameObjects)
        {
            if (!selectedObjects.Contains(obj))
            {
                selectedObjects.Add(obj);
                Debug.Log($"Simple Atlas Generator: Added '{obj.name}' to the list.");
            }
        }
        Repaint();
    }

    /// <summary>
    /// Generates a unique asset path by appending a number if the asset already exists.
    /// </summary>
    private string GetUniqueAssetPath(string basePath, string baseName, string extension)
    {
        string assetPath = Path.Combine(basePath, baseName + extension);
        int counter = 1;
        while (AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null)
        {
            assetPath = Path.Combine(basePath, $"{baseName}_{counter}{extension}");
            counter++;
        }
        return assetPath;
    }

    private void GenerateAtlas()
    {
        try
        {
            List<GameObject> validObjects = selectedObjects.Where(o => o != null).ToList();
            if (validObjects.Count == 0) return;

            string outputPath = RESOURCES_ROOT;
            CreateFolderIfNotExists(outputPath);

            // 1. Collect Renderers
            List<Renderer> renderers = new List<Renderer>();
            foreach (var obj in validObjects)
            {
                if (rendererScope == RendererScope.OnlyOnSelectedObjects)
                {
                    var r = obj.GetComponent<Renderer>();
                    if (r != null) renderers.Add(r);
                }
                else
                {
                    renderers.AddRange(obj.GetComponentsInChildren<Renderer>(true));
                }
            }
            List<(Renderer renderer, int materialIndex, Texture2D tex)> entries =
                new List<(Renderer, int, Texture2D)>();

            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;

                    Texture2D tex = Texture2D.whiteTexture;
                    if (mat.HasProperty("_MainTex"))
                        tex = mat.GetTexture("_MainTex") as Texture2D ?? Texture2D.whiteTexture;

                    entries.Add((r, m, tex));
                }
            }
            int totalCount = entries.Count;
            int maxAtlasSize = atlasSizes[selectedAtlasSizeIndex];

            List<Vector2Int> textureSizes = new List<Vector2Int>();
            List<CombinedTextureData> dataList = new List<CombinedTextureData>();

            for (int i = 0; i < entries.Count; i++)
            {
                var tex = entries[i].tex;

                int w = Mathf.Min(tex.width, maxAtlasSize - (padding * 2));
                int h = Mathf.Min(tex.height, maxAtlasSize - (padding * 2));

                float normalizedID = (i + 0.5f) / totalCount;

                textureSizes.Add(new Vector2Int(w, h));
                dataList.Add(new CombinedTextureData(tex, null, false, Color.white, normalizedID));
            }

            // 3. Run Packing Algorithm
            var placements = TexturePacker.Pack(textureSizes, maxAtlasSize, padding);

            // 4. Create the Actual Atlas Texture
            Texture2D diffuseAtlas = new Texture2D(maxAtlasSize, maxAtlasSize, TextureFormat.RGBA32, false);
            // Fill background with Black (Alpha 0)
            Color[] clearColor = Enumerable.Repeat(new Color(0, 0, 0, 0), maxAtlasSize * maxAtlasSize).ToArray();
            diffuseAtlas.SetPixels(clearColor);

            VisceraTextureSO registry = ScriptableObject.CreateInstance<VisceraTextureSO>();

            for (int i = 0; i < dataList.Count; i++)
            {
                if (!placements.ContainsKey(i)) continue;

                Rect rect = placements[i];
                var data = dataList[i];
                int w = (int)rect.width;
                int h = (int)rect.height;

                Color[] finalPixels;

                if (diffuseAtlasMode == DiffuseAtlasMode.Black)
                {
                    // Mode: Black - Fill RGB with 0, and Alpha with the ID
                    finalPixels = new Color[w * h];
                    Color idColor = new Color(0, 0, 0, data.normalizedID);
                    for (int p = 0; p < finalPixels.Length; p++)
                    {
                        finalPixels[p] = idColor;
                    }
                }
                else
                {
                    // Mode: Color - Sample the original texture
                    Texture2D resized = ResizeTexture(data.diffuseTexture, w, h, false);
                    finalPixels = resized.GetPixels();
                }

                diffuseAtlas.SetPixels((int)rect.x, (int)rect.y, w, h, finalPixels);

                // Save accurate UVs to Registry
                registry.entries.Add(new VisceraTextureSO.Entry
                {
                    atlasIndex = i,
                    uvOffset = new Vector2(rect.x / maxAtlasSize, rect.y / maxAtlasSize),
                    uvSize = new Vector2(rect.width / maxAtlasSize, rect.height / maxAtlasSize)
                });
            }

            diffuseAtlas.Apply();

            // 5. Save and Import
            string atlasName = "MaskAtlas_DiffuseAtlas";
            string atlasPath = GetUniqueAssetPath(outputPath, atlasName, ".png");
            File.WriteAllBytes(atlasPath, diffuseAtlas.EncodeToPNG());
            AssetDatabase.ImportAsset(atlasPath);

            // Configure Texture Settings (Crucial for ID masks)
            TextureImporter importer = AssetImporter.GetAtPath(atlasPath) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = (diffuseAtlasMode != DiffuseAtlasMode.Black); // Linear for IDs
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            registry.atlasTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            string registryPath = GetUniqueAssetPath(outputPath, "VisceraTextureSO", ".asset");
            AssetDatabase.CreateAsset(registry, registryPath);

            // Assign IDs to components
           int atlasEntryCursor = 0;
            for (int r = 0; r < renderers.Count; r++)
            {
                Renderer renderer = renderers[r];
                var materials = renderer.sharedMaterials;

                var idComp = renderer.GetComponent<VisceraDecalObjectID>()
                    ?? renderer.gameObject.AddComponent<VisceraDecalObjectID>();

                idComp.so = registry;
                List<int> materialAtlasIndices = new List<int>(materials.Length);

                for (int m = 0; m < materials.Length; m++)
                {
                    materialAtlasIndices.Add(atlasEntryCursor);
                    atlasEntryCursor++;
                }

                idComp.SetMaterialIndices(materialAtlasIndices);
            }


            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Success", $"Generated {diffuseAtlasMode} Atlas successfully!", "OK");
        }
        catch (System.Exception e) { Debug.LogError(e); }
    }

    private void CreateFolderIfNotExists(string path)
    {
        if (!AssetDatabase.IsValidFolder(path))
        {
            string parent = Path.GetDirectoryName(path);
            string folderName = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent))
            {
                CreateFolderIfNotExists(parent);
            }
            string newFolderPath = AssetDatabase.CreateFolder(parent, folderName);
            if (!string.IsNullOrEmpty(newFolderPath))
            {
                Debug.Log($"Simple Atlas Generator: Created folder '{newFolderPath}'.");
            }
            else
            {
                Debug.LogWarning($"Simple Atlas Generator: Failed to create folder '{path}'.");
            }
        }
    }

    private void EnsureTextureIsReadable(Texture2D texture)
    {
        if (texture == null) return;

        string path = AssetDatabase.GetAssetPath(texture);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer != null && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
            Debug.Log($"Simple Atlas Generator: Made texture '{texture.name}' readable.");
        }
    }

    /// <summary>
    /// Calculates the optimal number of rows and columns for the atlas grid to minimize unused space.
    /// Returns the number of rows, columns, and the texture size.
    /// </summary>
    private (int rows, int columns, int texSize) CalculateOptimalGrid(
        int texCount,
        int maxAtlasSize,
        List<TextureData> textureDataList
    )
    {
        int optimalRows = 0;
        int optimalColumns = 0;
        int minimalWaste = int.MaxValue;
        int optimalTexSize = 0;

        // Determine the maximum texture size among the collected textures
        int maxTexWidth = textureDataList.Max(td => td.texture.width);
        int maxTexHeight = textureDataList.Max(td => td.texture.height);
        int currentTexSize = Mathf.NextPowerOfTwo(Mathf.Max(maxTexWidth, maxTexHeight));

        // Start with the largest possible texture size and reduce if necessary
        while (currentTexSize >= 16)
        {
            for (int columns = 1; columns <= texCount; columns++)
            {
                int rows = Mathf.CeilToInt((float)texCount / columns);

                int atlasWidth = columns * (currentTexSize + padding * 2);
                int atlasHeight = rows * (currentTexSize + padding * 2);

                if (atlasWidth > maxAtlasSize || atlasHeight > maxAtlasSize)
                    continue;

                int waste = (columns * rows) - texCount;
                if (waste < minimalWaste)
                {
                    minimalWaste = waste;
                    optimalRows = rows;
                    optimalColumns = columns;
                    optimalTexSize = currentTexSize;

                    if (waste == 0)
                        break; // Perfect fit
                }
            }

            if (optimalRows > 0 && optimalColumns > 0)
                break; // Found a suitable grid

            currentTexSize /= 2;
        }

        if (optimalRows == 0 || optimalColumns == 0 || optimalTexSize == 0)
        {
            Debug.LogError("Simple Atlas Generator: Unable to fit textures into atlas within the maximum atlas size.");
            return (0, 0, 0);
        }

        Debug.Log($"Simple Atlas Generator: Optimal grid calculated - Rows: {optimalRows}, Columns: {optimalColumns}, Texture Size: {optimalTexSize}");
        return (optimalRows, optimalColumns, optimalTexSize);
    }
    private RenderTexture CreateAtlasRT(
        int width,
        int height,
        bool isNormal
    )
    {
        RenderTextureDescriptor desc = new RenderTextureDescriptor(width, height)
        {
            colorFormat = RenderTextureFormat.ARGB32,
            depthBufferBits = 0,
            msaaSamples = 1,
            mipCount = 1,                // NO mipmaps
            sRGB = !isNormal,             // linear for normals
            enableRandomWrite = true      // RW enabled
        };

        RenderTexture rt = new RenderTexture(desc);
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Clamp;
        rt.useMipMap = false;
        rt.autoGenerateMips = false;
        rt.Create();

        return rt;
    }

    private Texture2D CreateDiffuseAtlas(
        List<CombinedTextureData> combinedTextureDataList,
        int maxSize,
        string texturesPath,
        string atlasName,
        int padding,
        int rows,
        int columns,
        int texSize,
        bool saveAtlasToDisk = true // <--- extra flag for preview
    )
    {
        if (combinedTextureDataList.Count == 0)
        {
            Debug.LogError("Simple Atlas Generator: No diffuse textures to atlas.");
            return null;
        }

        int atlasWidth = columns * (texSize + padding * 2);
        int atlasHeight = rows * (texSize + padding * 2);

        Debug.Log($"Simple Atlas Generator: Creating diffuse atlas with {columns} columns and {rows} rows. Atlas size: {atlasWidth}x{atlasHeight}.");

        Texture2D atlas = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, false);
        atlas.name = atlasName + "_DiffuseAtlas";

        // Fill with white
        Color[] clearColors = Enumerable.Repeat(Color.white, atlasWidth * atlasHeight).ToArray();
        atlas.SetPixels(clearColors);

        for (int i = 0; i < combinedTextureDataList.Count; i++)
        {
            CombinedTextureData ctd = combinedTextureDataList[i];
            Texture2D diffuseTex = ctd.diffuseTexture;

            EnsureTextureIsReadable(diffuseTex);
            Texture2D resizedTex = ResizeTexture(diffuseTex, texSize, texSize, false);

            int row = i / columns;
            int col = i % columns;

            int x = col * (texSize + padding * 2) + padding;
            int y = row * (texSize + padding * 2) + padding;

            if (diffuseAtlasMode == DiffuseAtlasMode.Black)
            {
                float id = ctd.normalizedID;

                Color[] pixels = new Color[texSize * texSize];
                for (int p = 0; p < pixels.Length; p++)
                {
                    pixels[p] = new Color(0f, 0f, 0f, id);
                }

                atlas.SetPixels(x, y, texSize, texSize, pixels);

            }
            else
            {
                Color[] diffusePixels = resizedTex.GetPixels();

                if (ctd.applyColor)
                {
                    for (int p = 0; p < diffusePixels.Length; p++)
                        diffusePixels[p] *= ctd.color;
                }

                atlas.SetPixels(x, y, texSize, texSize, diffusePixels);
            }

        }

        atlas.Apply();

        if (saveAtlasToDisk)
        {
            // Save atlas as PNG
            string atlasPath = GetUniqueAssetPath(texturesPath, atlas.name, ".png");
            byte[] atlasBytes = atlas.EncodeToPNG();
            File.WriteAllBytes(atlasPath, atlasBytes);
            AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
            Texture2D importedAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);

            // Configure texture importer for diffuse
            TextureImporter atlasImporter = AssetImporter.GetAtPath(atlasPath) as TextureImporter;
            if (atlasImporter != null)
            {
                atlasImporter.isReadable = true;
                atlasImporter.textureCompression = TextureImporterCompression.Uncompressed;
                atlasImporter.sRGBTexture = true; // Use sRGB for diffuse textures
                atlasImporter.filterMode = FilterMode.Bilinear;
                atlasImporter.alphaSource = TextureImporterAlphaSource.FromInput;
                atlasImporter.alphaIsTransparency = false;
                atlasImporter.SaveAndReimport();

                return importedAtlas;
            }
            return importedAtlas; // fallback if importer is null
        }
        else
        {
            // Preview mode: just return the in-memory atlas
            return atlas;
        }
    }

    private Texture2D CreateNormalAtlas(
        List<CombinedTextureData> combinedTextureDataList,
        int maxSize,
        string normalTexturesPath,
        string atlasName,
        int padding,
        int rows,
        int columns,
        int texSize,
        bool saveAtlasToDisk = true
    )
    {
        if (combinedTextureDataList.Count == 0)
        {
            Debug.LogError("Simple Atlas Generator: No normal textures to atlas.");
            return null;
        }

        int atlasWidth = columns * (texSize + padding * 2);
        int atlasHeight = rows * (texSize + padding * 2);

        Debug.Log($"Simple Atlas Generator: Creating normal atlas with {columns} columns and {rows} rows. Atlas size: {atlasWidth}x{atlasHeight}.");

        Texture2D atlas = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, false);
        atlas.name = atlasName + "_NormalAtlas";

        // Fill with flat normal color
        Color flatNormalColor = new Color(0.5f, 0.5f, 1f, 1f);
        Color[] clearColors = Enumerable.Repeat(flatNormalColor, atlasWidth * atlasHeight).ToArray();
        atlas.SetPixels(clearColors);

        for (int i = 0; i < combinedTextureDataList.Count; i++)
        {
            CombinedTextureData ctd = combinedTextureDataList[i];
            Texture2D normalTex = ctd.normalTexture;

            if (normalTex == null) continue; // skip if no normal

            EnsureTextureIsReadable(normalTex);
            Texture2D resizedTex = ResizeTexture(normalTex, texSize, texSize, true);

            int row = i / columns;
            int col = i % columns;

            int x = col * (texSize + padding * 2) + padding;
            int y = row * (texSize + padding * 2) + padding;

            Color[] normalPixels = resizedTex.GetPixels();
            atlas.SetPixels(x, y, texSize, texSize, normalPixels);
        }

        atlas.Apply();

        if (saveAtlasToDisk)
        {
            // Save atlas as PNG
            string atlasPath = GetUniqueAssetPath(normalTexturesPath, atlas.name, ".png");
            byte[] atlasBytes = atlas.EncodeToPNG();
            File.WriteAllBytes(atlasPath, atlasBytes);
            AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
            Texture2D importedAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);

            // Configure importer for normal map
            TextureImporter atlasImporter = AssetImporter.GetAtPath(atlasPath) as TextureImporter;
            if (atlasImporter != null)
            {
                atlasImporter.isReadable = true;
                atlasImporter.textureCompression = TextureImporterCompression.Uncompressed;
                atlasImporter.textureType = TextureImporterType.NormalMap;
                atlasImporter.wrapMode = TextureWrapMode.Clamp;
                atlasImporter.filterMode = FilterMode.Bilinear;
                atlasImporter.mipmapEnabled = false;
                atlasImporter.sRGBTexture = false; // Use linear for normal maps
                atlasImporter.SaveAndReimport();

                return importedAtlas;
            }
            return importedAtlas; // fallback if importer is null
        }
        else
        {
            return atlas;
        }
    }

    private Texture2D ResizeTexture(Texture2D source, int newWidth, int newHeight, bool isNormalMap)
    {
        // Ensure dimensions are at least 1x1
        newWidth = Mathf.Max(1, newWidth);
        newHeight = Mathf.Max(1, newHeight);

        RenderTextureDescriptor descriptor = new RenderTextureDescriptor(newWidth, newHeight, RenderTextureFormat.ARGB32, 0)
        {
            sRGB = !isNormalMap
        };
        
        RenderTexture rt = RenderTexture.GetTemporary(descriptor);
        rt.filterMode = FilterMode.Point; // Use Point to keep ID edges sharp

        Graphics.Blit(source, rt);
        Texture2D resized = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false, !isNormalMap);
        RenderTexture.active = rt;
        resized.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
        resized.Apply();

        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        return resized;
    }

    /// <summary>
    /// Generates a flat normal map (points upwards).
    /// </summary>
    private Texture2D GenerateFlatNormalMap(int texSize)
    {
        Texture2D flatNormal = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        Color32 flatColor = new Color32(128, 128, 255, 255); // Represents a flat normal

        Color32[] pixels = new Color32[texSize * texSize];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = flatColor;
        }

        flatNormal.SetPixels32(pixels);
        flatNormal.Apply();
        flatNormal.name = "FlatNormal";

        // Save the flat normal texture
        string folderPath = RESOURCES_ROOT;
        CreateFolderIfNotExists(folderPath);
        string flatNormalPath = Path.Combine(folderPath, "FlatNormal.png");

        byte[] bytes = flatNormal.EncodeToPNG();
        File.WriteAllBytes(flatNormalPath, bytes);
        AssetDatabase.ImportAsset(flatNormalPath, ImportAssetOptions.ForceUpdate);
        Texture2D importedFlatNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(flatNormalPath);

        // Ensure it's marked as a normal map
        TextureImporter importer = AssetImporter.GetAtPath(flatNormalPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
            Debug.Log($"Simple Atlas Generator: Saved flat normal map at '{flatNormalPath}'.");
        }

        return importedFlatNormal;
    }
    public class TexturePacker
    {
        private class Node
        {
            public Rect rect;
            public Node left;
            public Node right;
            public bool occupied;

            public Node(float x, float y, float w, float h) { rect = new Rect(x, y, w, h); }

            public Node Insert(int w, int h, int padding)
            {
                // If we are a branch, try to insert into children
                if (left != null)
                {
                    Node node = left.Insert(w, h, padding);
                    return node ?? right.Insert(w, h, padding);
                }

                int pW = w + padding * 2;
                int pH = h + padding * 2;

                // Check if this leaf can fit the texture
                if (occupied || pW > rect.width || pH > rect.height)
                    return null;

                // If it's a perfect fit, mark occupied
                if (pW == rect.width && pH == rect.height)
                {
                    occupied = true;
                    return this;
                }

                // Otherwise, split this node into two children
                float dw = rect.width - pW;
                float dh = rect.height - pH;

                if (dw > dh) // Split horizontally
                {
                    left = new Node(rect.x, rect.y, pW, rect.height);
                    right = new Node(rect.x + pW, rect.y, rect.width - pW, rect.height);
                }
                else // Split vertically (This creates the next row!)
                {
                    left = new Node(rect.x, rect.y, rect.width, pH);
                    right = new Node(rect.x, rect.y + pH, rect.width, rect.height - pH);
                }

                return left.Insert(w, h, padding);
            }
        }

        public static Dictionary<int, Rect> Pack(List<Vector2Int> sizes, int atlasSize, int padding)
        {
            Node root = new Node(0, 0, atlasSize, atlasSize);
            var results = new Dictionary<int, Rect>();

            // IMPORTANT: Sort by Area or Height to prevent "First Row" hogging
            var sorted = sizes
                .Select((size, index) => new { size, index })
                .OrderByDescending(x => x.size.y) // Sort by Height
                .ThenByDescending(x => x.size.x)  // Then by Width
                .ToList();

            foreach (var item in sorted)
            {
                Node node = root.Insert(item.size.x, item.size.y, padding);
                if (node != null)
                {
                    results[item.index] = new Rect(node.rect.x + padding, node.rect.y + padding, item.size.x, item.size.y);
                }
                else
                {
                    Debug.LogWarning($"Could not fit texture index {item.index} ({item.size.x}x{item.size.y}) into {atlasSize} atlas.");
                }
            }
            return results;
        }
    }
}

