using System;
using System.Collections.Generic;
using Unity.Collections;           // DOTS-native collections (job/Burst safe, must Dispose)
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using QuadTree_MainThreadVersion;
using static Unity.Mathematics.math;
using System.Linq;
using TMPro;

namespace QuadTree_MainThreadVersion
{
    public class QuadTreeIntersectionDetection : MonoBehaviour
    {
        [SerializeField] bool logCollisions = true;
        private readonly List<Canvas> _canvases = new();
        private readonly List<Graphic> _graphics = new();
        private readonly List<Bounds> _bounds = new();
        private NativeQuadtree<int> _tree;
        private bool _treeCreated;
        private bool needsUpdate = false;
        void Awake()
        {
            _canvases.Clear();
            _canvases.AddRange(FindObjectsOfType<Canvas>(false));
        }
        void OnEnable()
        {
            Canvas.willRenderCanvases += OnCanvasRebuilt;
        }
        void OnDisable()
        {
            if (_treeCreated)
            {
                _tree.Dispose();
                _treeCreated = false;
            }
            Canvas.willRenderCanvases -= OnCanvasRebuilt;
        }
        private void OnCanvasRebuilt()
        {
            // Called once per canvas rebuild (just before rendering)
            needsUpdate = true;
        }
        void Update()
        {
            if (!needsUpdate) return;
            needsUpdate = false;

            RebuildGraphicsAndBounds();
            RebuildQuadtree();
            CheckCollisionsViaQuadtree();
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

                    // exclude if opacity < 1
                    // float alpha = g.canvasRenderer.GetAlpha(); // works for all UI graphics
                    // if (alpha < 0.999f) continue; // tolerance for float errors
                    // if (g.color.a < 0.999f) continue;
                    // if (g is Image img && img.sprite == null) continue;
                    if (!IsFullyOpaque(g)) continue;

                    var b = GetGraphicWorldAABB(g);

                    // Ignore objects that are too small (basically invisible)
                    if (b.size.sqrMagnitude < 1e-8f) continue;
                    _graphics.Add(g);
                    _bounds.Add(b);
                    if (g is Image image) image.useSpriteMesh = true;
                    if (g is TMP_Text) Debug.Log("Text found");
                }
            }
        }
        static float GetEffectiveAlpha(Graphic g)
        {
            if (!g) return 0f;
            float a = g.color.a;

            // Parent CanvasGroups (respect ignoreParentGroups)
            Transform t = g.transform;
            while (t != null)
            {
                if (t.TryGetComponent<CanvasGroup>(out var cg))
                {
                    a *= Mathf.Clamp01(cg.alpha);
                    if (cg.ignoreParentGroups) break; // don't include ancestors above this group
                }
                t = t.parent;
            }

            // CanvasRenderer alpha (can be driven by CrossFadeAlpha/animations)
            a *= Mathf.Clamp01(g.canvasRenderer.GetAlpha());

            // Material color alpha if present
            if (g is MaskableGraphic mg && mg.material != null && mg.material.HasProperty("_Color"))
                a *= mg.material.color.a;
            return Mathf.Clamp01(a);
        }
        static bool IsFullyOpaque(Graphic g) => GetEffectiveAlpha(g) >= 0.999f; // treat 1.0 with epsilon

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

            var candidates = new NativeList<int>(Allocator.Temp);
            var unique = new NativeHashSet<int>(64, Allocator.Temp);

            for (int i = 0; i < _graphics.Count; i++)
            {
                var aAabb = ToAABB2D(_bounds[i]);
                candidates.Clear();
                unique.Clear();

                var collector = new CandidateCollector(ref candidates, ref unique);
                _tree.Range(aAabb, ref collector);

                bool isOccluded = false;
                long orderA = GetRenderKey(_graphics[i]);

                for (int k = 0; k < candidates.Length; k++)
                {
                    int j = candidates[k];
                    if (j == i) continue; // only skip self (do NOT skip j < i!)

                    var bAabb = ToAABB2D(_bounds[j]);
                    if (!IsContained2D(aAabb, bAabb)) continue;

                    long orderB = GetRenderKey(_graphics[j]);

                    // B must be drawn AFTER A to occlude it
                    if (orderB > orderA)
                    {
                        if (logCollisions)
                            Debug.Log($"{_graphics[i].name} is CONTAINED by (and behind) {_graphics[j].name}. Culling {_graphics[i].name}");

                        isOccluded = true;
                        break; // A is hidden by at least one top object
                    }
                }

                _graphics[i].canvasRenderer.cull = isOccluded;
            }
        }

        private bool IsContained(Bounds a, Bounds b)
        {
            return b.Contains(a.min) && b.Contains(a.max);
        }

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
        private struct CandidateCollector : IQuadtreeRangeVisitor<int>
        {
            private NativeList<int> _out;
            private NativeHashSet<int> _seen;

            public CandidateCollector(ref NativeList<int> outList, ref NativeHashSet<int> seen)
            {
                _out = outList;
                _seen = seen;
            }

            // Called for every object in overlapping quadtree leaf
            public bool OnVisit(int id, AABB2D objBounds, AABB2D queryRange)
            {
                // Add unique IDs only
                if (_seen.Add(id))
                    _out.Add(id);
                return true;
            }
        }

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
}
