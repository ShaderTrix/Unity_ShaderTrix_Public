using System;
using System.Collections.Generic;
using Unity.Collections;        // DOTS-native collections (job/Burst safe, must Dispose)
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using NativeTreesJobs;
using static Unity.Mathematics.math;
using System.Linq;
using Unity.Burst;
using Unity.Jobs;

public class QuadTreeIntersectionDetection : MonoBehaviour
{
    private readonly List<Canvas> _canvases = new();
    private readonly List<Graphic> _graphics = new();
    private readonly List<Bounds> _bounds = new();
    private NativeQuadtree<int> _tree;
    private bool _treeCreated;
    private int _lastHierarchyCount;
    private bool needsUpdate = false;
    void Awake()
    {
        _lastHierarchyCount = transform.hierarchyCount;
        RefreshCanvases();
    }
    void OnTransformChildrenChanged() => RefreshCanvases();
    // void OnEnable()
    // {
    //     Canvas.willRenderCanvases += OnCanvasRebuilt;
    // }
    void OnDisable()
    {
        if (_treeCreated)
        {
            _tree.Dispose();
            _treeCreated = false;
        }
        // Canvas.willRenderCanvases -= OnCanvasRebuilt;
    }
    // private void OnCanvasRebuilt()
    // {
    //     // Called once per canvas rebuild (just before rendering)
    //     needsUpdate = true;
    // }
    void LateUpdate()
    {
        if (transform.hierarchyCount != _lastHierarchyCount)
        {
            _lastHierarchyCount = transform.hierarchyCount;
            RefreshCanvases();
        }
        // if (!needsUpdate) return;
        // needsUpdate = false;
        if (Time.frameCount % 5 != 0) return;

        RebuildGraphicsAndBounds();
        RebuildQuadtree();
        CheckCollisionsViaQuadtree();
    }
    private void RefreshCanvases()
    {
        // Debug.Log("Refreshing Canvases");
        _canvases.Clear();
        GetComponentsInChildren(includeInactive: false, result: _canvases);
        // _canvases.AddRange(FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
    }
    #region Build inputs

    void RebuildGraphicsAndBounds()
    {
        _graphics.Clear();
        _bounds.Clear();
        foreach (var canvas in _canvases)
        {
            var graphics = GraphicRegistry.GetGraphicsForCanvas(canvas);
            for (int i = 0; i < graphics.Count; i++)
            {
                var g = graphics[i];
                if (!g || !g.isActiveAndEnabled) continue;

                if (!QuadTreeRules.IsValidGraphic(g)) continue;

                var b = GetGraphicWorldAABB(g);

                // Ignore objects that are too small (basically invisible)
                if (b.size.sqrMagnitude < 1e-8f) continue;
                _graphics.Add(g);
                _bounds.Add(b);
                if (g is Image image) image.useSpriteMesh = true;
            }
        }
    }
    #endregion

    #region Quadtree

    void RebuildQuadtree()
    {
        if (_graphics.Count == 0)
        {
            if (_treeCreated) { _tree.Clear(); }
            return;
        }
        var worldBB = _bounds[0];

        foreach (var bound in _bounds.Skip(1))
            worldBB.Encapsulate(bound);

        var pad = new Vector3(0.01f, 0.01f, 0f); //??
        worldBB.Encapsulate(worldBB.center + pad);
        worldBB.Encapsulate(worldBB.center - pad);

        var treeBounds = ToAABB2D(worldBB);

        const int objectsPerNode = 16;
        const int maxDepth = 12;
        int initialCapacity = _graphics.Count;
        if (!_treeCreated)
        {
            _tree = new NativeQuadtree<int>(treeBounds, objectsPerNode, maxDepth, Allocator.Persistent, initialCapacity);
            _treeCreated = true;
        }
        else
        {
            var old = _tree.Bounds;
            var oldSize = old.Size;
            var newSize = treeBounds.Size;

            bool sizeShifted = any(abs(newSize - oldSize) > 0.25f * (oldSize + new float2(1e-5f)));

            if (sizeShifted || !_tree.Bounds.Center.Equals(treeBounds.Center))
            {
                _tree.Dispose();
                _tree = new NativeQuadtree<int>(treeBounds, objectsPerNode, maxDepth, Allocator.Persistent, initialCapacity);
            }
            else
            {
                _tree.Clear();
            }
        }
        for (int i = 0; i < _graphics.Count; i++)
        {
            var aabb = ToAABB2D(_bounds[i]);
            _tree.Insert(i, aabb);
        }
    }

    #endregion

    #region Collision via range queries

    void CheckCollisionsViaQuadtree()
    {
        if (!_treeCreated || _graphics.Count == 0) return;

        var count = _graphics.Count;
        var boundsA = new NativeArray<AABB2D>(count, Allocator.TempJob);
        var keys = new NativeArray<long>(count, Allocator.TempJob);
        var results = new NativeArray<bool>(count, Allocator.TempJob);

        for (int i = 0; i < count; i++)
        {
            boundsA[i] = ToAABB2D(_bounds[i]);
            keys[i] = GetRenderKey(_graphics[i]);
        }

        // ---------- 1) MAIN THREAD: collect candidates from quadtree ----------
        // Reserve ~8 candidates per item as a starting heuristic
        var candidateMap = new NativeParallelMultiHashMap<int, int>(count * 8, Allocator.TempJob);

        // Reuse temporaries to avoid per-iteration allocs if you want; Temp is fine for 500
        for (int i = 0; i < count; i++)
        {
            var aAabb = boundsA[i];
            var list = new NativeList<int>(Allocator.Temp);
            var dedupe = new NativeHashSet<int>(64, Allocator.Temp);

            var collector = new CandidateCollector(ref list, ref dedupe);
            _tree.Range(aAabb, ref collector);          // <— tree stays on main thread

            for (int k = 0; k < list.Length; k++)
                candidateMap.Add(i, list[k]);

            list.Dispose();
            dedupe.Dispose();
        }

        // ---------- 2) JOB: do containment + depth check in parallel ----------
        var job = new OcclusionJob
        {
            bounds = boundsA,
            renderKeys = keys,
            candidates = candidateMap,
            occluded = results
        };

        job.Schedule(count, 64).Complete();

        // ---------- 3) Apply results on main thread ----------
        for (int i = 0; i < count; i++)
        {
            _graphics[i].canvasRenderer.cull = results[i];
        }
        // ---------- 4) Dispose ----------
        candidateMap.Dispose();
        boundsA.Dispose();
        keys.Dispose();
        results.Dispose();
    }

    [BurstCompile]
    struct OcclusionJob : IJobParallelFor
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<AABB2D> bounds;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<long> renderKeys;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeParallelMultiHashMap<int, int> candidates;

        public NativeArray<bool> occluded;

        public void Execute(int i)
        {
            var aAabb = bounds[i];
            var orderA = renderKeys[i];
            bool hidden = false;

            if (candidates.TryGetFirstValue(i, out int j, out var it))
            {
                do
                {
                    if (j == i) continue;

                    var bAabb = bounds[j];
                    if (!IsContained2D(aAabb, bAabb)) continue;

                    if (renderKeys[j] > orderA)
                    {
                        hidden = true;
                        break;
                    }
                }
                while (candidates.TryGetNextValue(out j, ref it));
            }

            occluded[i] = hidden;
        }
    }

    // Same as yours; used only on main thread now
    private struct CandidateCollector : IQuadtreeRangeVisitor<int>
    {
        private NativeList<int> _out;
        private NativeHashSet<int> _seen;

        public CandidateCollector(ref NativeList<int> outList, ref NativeHashSet<int> seen)
        {
            _out = outList;
            _seen = seen;
        }

        public bool OnVisit(int id, AABB2D objBounds, AABB2D queryRange)
        {
            if (_seen.Add(id)) _out.Add(id);
            return true;
        }
    }


    // [BurstCompile]
    // struct OcclusionJob : IJobParallelFor
    // {
    //     [ReadOnly] public NativeArray<AABB2D> bounds;
    //     [ReadOnly] public NativeQuadtree<int>.ReadOnly tree;
    //     [ReadOnly] public NativeArray<long> renderKeys;

    //     public NativeArray<bool> occluded; // output

    //     public void Execute(int i)
    //     {
    //         var aAabb = bounds[i];
    //         var candidates = new NativeList<int>(Allocator.Temp);
    //         var unique = new NativeHashSet<int>(64, Allocator.Temp);

    //         var collector = new CandidateCollector(ref candidates, ref unique);
    //         tree.Range(aAabb, ref collector);

    //         long orderA = renderKeys[i];
    //         bool isOccluded = false;

    //         for (int k = 0; k < candidates.Length; k++)
    //         {
    //             int j = candidates[k];
    //             if (j == i) continue;

    //             var bAabb = bounds[j];
    //             if (!IsContained2D(aAabb, bAabb)) continue;

    //             if (renderKeys[j] > orderA)
    //             {
    //                 isOccluded = true;
    //                 break;
    //             }
    //         }

    //         occluded[i] = isOccluded;

    //         candidates.Dispose();
    //         unique.Dispose();
    //     }
    // }

    // private bool IsContained(Bounds a, Bounds b)
    // {
    //     return b.Contains(a.min) && b.Contains(a.max);
    // }

    /// <summary>
    /// Get a numeric rendering order for a Graphic.
    /// Higher values = rendered later (on top).
    /// </summary>
    private long GetRenderKey(Graphic g)
    {
        if (!g || !g.canvas) return long.MinValue;

        var c = g.canvas;

        // Sorting layer value (larger means drawn later), then sorting order, then absolute depth within that canvas
        int layerVal = SortingLayer.GetLayerValueFromID(c.sortingLayerID); // eg:background/Default/UI basically the sorting layer set in the canvas
        int order = c.sortingOrder;                                      // which sorting layer the canvas is in to decide which canvas is on top 
        int absDepth = g.canvasRenderer.absoluteDepth;                      //within each layer we can set a sorting depth (int, 0-1000)

        // Pack into a 64-bit key: layer -> order -> depth
        // Offset "order" so negatives (if any) sort before positives.
        //checked/unchecked gives us an overflow exception whenever value overflows the bit length
        //setting to unchecked tells to ignore the exception and just wrap around and give the binary result
        //packing all three ints into one long 
        unchecked
        {
            long key = 0;
            key |= ((long)layerVal & 0xFFFFL) << 48;         // 16 bits
            key |= ((long)(order + 32768) & 0xFFFFL) << 32;  // 16 bits with offset
            key |= ((long)(absDepth < 0 ? 0 : absDepth) & 0xFFFFFFFFL); // 32 bits
            return key;//combining all the tree parameters to get a final depth(int) value for the actual sorting depth & layer
        }
    }
    static bool IsContained2D(AABB2D a, AABB2D b, float eps = 1e-5f)
    {
        // a fully inside b?
        return a.min.x >= b.min.x - eps &&
               a.min.y >= b.min.y - eps &&
               a.max.x <= b.max.x + eps &&
               a.max.y <= b.max.y + eps;
    }

    /// <summary>
    /// The quadtree itself decides which objects to feed into the collector:
    // It looks at the query range (A’s bounding box).
    // It finds all quadtree leaves overlapping that range.
    // It calls OnVisit for every object index in those leaves.
    // The collector’s job is only to filter duplicates and store them for later.
    // Custom visitor struct: collects quadtree results without duplicates
    /// </summary>        
    // private struct CandidateCollector : IQuadtreeRangeVisitor<int>
    // {
    //     private NativeList<int> _out;
    //     private NativeHashSet<int> _seen;

    //     public CandidateCollector(ref NativeList<int> outList, ref NativeHashSet<int> seen)
    //     {
    //         _out = outList;
    //         _seen = seen;
    //     }

    //     // Called for every object in overlapping quadtree leaf
    //     public bool OnVisit(int id, AABB2D objBounds, AABB2D queryRange)
    //     {
    //         // Add unique IDs only
    //         if (_seen.Add(id))
    //             _out.Add(id);
    //         return true;
    //     }
    // }

    #endregion

    #region Helpers: bounds + sprite mesh vertices

    // Convert Unity Bounds -> AABB2D struct (float2 min/max)
    static AABB2D ToAABB2D(Bounds b)
    {
        float2 mn = new float2(b.min.x, b.min.y);
        float2 mx = new float2(b.max.x, b.max.y);
        return new AABB2D(mn, mx);
    }

    // Compute world-space bounding box for a Graphic
    static Bounds GetGraphicWorldAABB(Graphic g)
    {
        if (g is Image img)
        {
            // If Image uses sprite mesh, compute from sprite vertices
            if (img.useSpriteMesh && img.sprite != null && img.sprite.vertices != null && img.sprite.vertices.Length > 0)
            {
                var verts = GetImageSpriteMeshWorldVerts(img);
                if (verts.Count == 0) return new Bounds(Vector3.zero, Vector3.zero);

                var b = new Bounds(verts[0], Vector3.zero);
                for (int i = 1; i < verts.Count; i++) b.Encapsulate(verts[i]);
                return b;
            }
            else
            {
                // Otherwise, just use RectTransform corners
                var corners = new Vector3[4];
                g.rectTransform.GetWorldCorners(corners);
                var bb = new Bounds(corners[0], Vector3.zero);
                for (int i = 1; i < 4; i++) bb.Encapsulate(corners[i]);
                return bb;
            }
        }

        // Default empty bounds
        return new Bounds(Vector3.zero, Vector3.zero);
    }

    // Get world-space verts of sprite mesh for more precise bounds
    static List<Vector3> GetImageSpriteMeshWorldVerts(Image img)
    {
        var sprite = img.sprite;
        var verts2 = sprite.vertices;
        if (verts2 == null || verts2.Length == 0) return ListPool<Vector3>.Get();

        var sSize = sprite.rect.size / sprite.pixelsPerUnit;

        var r = img.GetPixelAdjustedRect();
        float drawW = r.width, drawH = r.height;

        // Handle aspect ratio preservation
        if (img.preserveAspect)
        {
            float spriteAspect = sSize.x / sSize.y;
            float rectAspect = drawW / drawH;
            if (spriteAspect > rectAspect) drawH = drawW / spriteAspect;
            else drawW = drawH * spriteAspect;
        }

        Vector2 scale = new Vector2(drawW / sSize.x, drawH / sSize.y);
        Vector2 pivot = img.rectTransform.pivot;
        Vector2 offset = new Vector2((0.5f - pivot.x) * r.width, (0.5f - pivot.y) * r.height);

        var outVerts = ListPool<Vector3>.Get(verts2.Length);
        for (int i = 0; i < verts2.Length; i++)
        {
            Vector2 v = verts2[i];
            var pLocal = new Vector3(v.x * scale.x + offset.x,
                                     v.y * scale.y + offset.y, 0f);
            outVerts.Add(img.rectTransform.TransformPoint(pLocal));
        }
        return outVerts;
    }

    // Very simple pooled List to avoid GC allocations when extracting verts
    static class ListPool<T>
    {
        static readonly Stack<List<T>> Pool = new Stack<List<T>>();

        public static List<T> Get(int capacity = 0)
        {
            if (Pool.Count == 0) return capacity > 0 ? new List<T>(capacity) : new List<T>();
            var l = Pool.Pop();
            l.Clear();
            if (capacity > 0 && l.Capacity < capacity) l.Capacity = capacity;
            return l;
        }

        public static void Release(List<T> l)
        {
            if (l != null) Pool.Push(l);
        }
    }

    #endregion
}
