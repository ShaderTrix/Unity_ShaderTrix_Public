using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace NativeTreesJobs
{
    public partial struct NativeQuadtree<T> : INativeDisposable where T : unmanaged
    {
        //outer struct scope
        //outer scope is needed for data storing outside of the job structure...like the c# side data
        //needed internal because job is defined in the same assembly..so for faster,unmanaged,unmodifyable access to the variables
        //jobs will be updating here
        internal NativeParallelHashMap<uint, int> nodes;
        internal NativeParallelMultiHashMap<uint, ObjWrapper> objects;

        internal readonly int maxDepth;
        internal readonly int objectsPerNode;
        internal readonly AABB2D bounds;
        internal readonly float2 boundsCenter;
        internal readonly float2 boundsExtents;
        internal readonly float2 boundsQuarterSize;

        //an obj data will store these
        internal readonly struct ObjWrapper
        {
            public readonly AABB2D bounds;
            public readonly T obj;
            public ObjWrapper(T obj, AABB2D bounds) { this.obj = obj; this.bounds = bounds; }
        }

        //Represents a nodes center and its halfs half quad
        internal readonly struct QuarterSizeBounds
        {
            public readonly float2 nodeCenter;
            public readonly float2 nodeQuarterSize;
            public QuarterSizeBounds(float2 center, float2 qsize) { nodeCenter = center; nodeQuarterSize = qsize; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static QuarterSizeBounds GetQuad(in QuarterSizeBounds parent, int index)
            {
                return new QuarterSizeBounds(
                    parent.nodeCenter + QuadCenterOffsets[index] * parent.nodeQuarterSize,
                    0.5f * parent.nodeQuarterSize);
            }
        }
        //node center and its half 
        internal readonly struct ExtentsBounds
        {
            public readonly float2 nodeCenter;
            public readonly float2 nodeExtents;
            public ExtentsBounds(float2 c, float2 e) { nodeCenter = c; nodeExtents = e; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static ExtentsBounds GetQuad(in ExtentsBounds parent, int index)
            {
                float2 quadExtents = 0.5f * parent.nodeExtents;
                return new ExtentsBounds(parent.nodeCenter + QuadCenterOffsets[index] * quadExtents, quadExtents);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static AABB2D GetBounds(in ExtentsBounds ce) => new AABB2D(ce.nodeCenter - ce.nodeExtents, ce.nodeCenter + ce.nodeExtents);
        }

        public const int MaxDepth = 8 * sizeof(int) / 2 - 1;

        private static readonly float2[] QuadCenterOffsets =
        {
            new float2(-1, -1), new float2( 1, -1),
            new float2(-1,  1), new float2( 1,  1)
        };

        //binary masks to check two quadrants                          
        private static readonly int[] QuadMasks = { 0b00_11,    //AABB must extend left & down
                                                    0b01_10,    //AABB must extend right & down
                                                    0b10_01,    //AABB must extend left and up
                                                    0b11_00 };  //AABB must extend right and up

        //bit packing shifts the last two binary index by 2 places and inserts quadIndex into there
        //so every time it enters a node its encodes the index into itself so that it knows the way back to root
        //Damn cool
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetQuadId(uint parent, int quadIndex) => (parent << 2) | (uint)quadIndex;

        //this is to check which quadrant the AABB overlaps from the nodes center
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetBoundsMask(float2 nodeCenter, AABB2D aabb)
        {
            int offMin = math.bitmask(aabb.min.xyxy <= nodeCenter.xyxy) >> 2;
            return offMin | (math.bitmask(aabb.max.xyxy >= nodeCenter.xyxy) << 2);
        }


        //Readonly data for the Job..copy of the outer scope 
        //Inner scope
        public readonly struct ReadOnly
        {
            [ReadOnly] private readonly NativeParallelHashMap<uint, int> nodes;
            [ReadOnly] private readonly NativeParallelMultiHashMap<uint, ObjWrapper> objects;

            private readonly int maxDepth;
            private readonly int objectsPerNode;
            private readonly AABB2D bounds;
            private readonly float2 boundsCenter;
            private readonly float2 boundsQuarterSize;

            //pasting the puter scopes value into this scope
            internal ReadOnly(in NativeQuadtree<T> tree)
            {
                nodes = tree.nodes;
                objects = tree.objects;
                bounds = tree.bounds;
                maxDepth = tree.maxDepth;
                objectsPerNode = tree.objectsPerNode;
                boundsCenter = tree.boundsCenter;
                boundsQuarterSize = tree.boundsQuarterSize;
            }

            public AABB2D Bounds => bounds;


            //Entry point..this is the function which is run first for the root node id= 1
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Range<U>(in AABB2D range, ref U visitor) where U : struct, IQuadtreeRangeVisitor<T>
            {
                RangeNext(range, 1, new QuarterSizeBounds(boundsCenter, boundsQuarterSize), ref visitor, 0);
            }

            //Checks each of the roots child nodes if the AABB intersects with which node
            //if its finds the intersection in respective node executes a range query
            //generating an id for each node it enters,and marks the exact node the AABB is intersection is
            //coarse tree traversal test to check which AABBS intersect with one another
            private bool RangeNext<U>(in AABB2D range, uint nodeId, in QuarterSizeBounds qsb, ref U visitor, int parentDepth)
                where U : struct, IQuadtreeRangeVisitor<T>
            {
                parentDepth++;
                int rangeMask = GetBoundsMask(qsb.nodeCenter, range);

                for (int i = 0; i < 4; i++)
                {
                    int quadMask = QuadMasks[i];
                    if ((rangeMask & quadMask) != quadMask) continue;

                    uint childId = GetQuadId(nodeId, i);
                    if (nodes.TryGetValue(childId, out int objectCount) &&
                        !Range(range, childId, QuarterSizeBounds.GetQuad(qsb, i), objectCount, ref visitor, parentDepth))
                        return false;
                }
                return true;
            }

            //Handles the actual leaf nodes keeps recursively going into its child if and calls RangeNext to do same thing there 
            //until it reaches the exact AABB intersection or reaches max depth
            //Range and Range next only handle traversal
            //The range visitor handles what to do when you find the leaf node            
            private bool Range<U>(in AABB2D range, uint nodeId, in QuarterSizeBounds qsb, int objectCount, ref U visitor, int depth)
                where U : struct, IQuadtreeRangeVisitor<T>
            {
                // Leaf?
                if (objectCount <= objectsPerNode || depth == maxDepth)
                {
                    //Checks for the first object, for the nodeID..to find which wrappedObj is matching with the nodeID
                    if (objects.TryGetFirstValue(nodeId, out var wrappedObj, out var it))
                    {
                        do
                        {
                            //searches for the first object that intersects with the leaf 
                            if (!visitor.OnVisit(wrappedObj.obj, wrappedObj.bounds, range)) return false;
                        } while (objects.TryGetNextValue(out wrappedObj, ref it));//Checks for other objects in the leaf
                    }
                    return true;
                }
                //not leaf? go back to checking other objects for intersections
                return RangeNext(range, nodeId, qsb, ref visitor, depth);
            }
        }

        public ReadOnly AsReadOnly() => new ReadOnly(this);

        //shortcut constructor with default values 
        public NativeQuadtree(AABB2D bounds, Allocator allocator) : this(bounds, 16, 8, allocator) { }
        //main constructor..fils the nativeParallelMultiHashMap
        public NativeQuadtree(AABB2D bounds, int objectsPerNode, int maxDepth, Allocator allocator, int initialCapacity = 0)
        {
            if (maxDepth <= 1 || maxDepth > MaxDepth) throw new ArgumentOutOfRangeException(nameof(maxDepth));
            if (!bounds.IsValid) throw new ArgumentException("Bounds must be valid");

            objects = new NativeParallelMultiHashMap<uint, ObjWrapper>(initialCapacity, allocator);
            nodes = new NativeParallelHashMap<uint, int>(math.max(1, initialCapacity / math.max(1, objectsPerNode)), allocator);

            this.objectsPerNode = objectsPerNode;
            this.maxDepth = maxDepth;
            this.bounds = bounds;
            this.boundsCenter = bounds.Center;
            this.boundsExtents = bounds.Size / 2;
            this.boundsQuarterSize = boundsExtents / 2;
        }
        //clearing of the hashMap
        public void Clear() { objects.Clear(); nodes.Clear(); }

        public void Dispose() { nodes.Dispose(); objects.Dispose(); }

        public JobHandle Dispose(JobHandle deps)
            => JobHandle.CombineDependencies(nodes.Dispose(deps), objects.Dispose(deps));

        public AABB2D Bounds => bounds;

        //given an object attempts to store into a node and assigning it a nodeID
        //Root node query
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Insert(T obj, AABB2D b)
        {
            var wrap = new ObjWrapper(obj, b);
            InsertNext(1, new QuarterSizeBounds(boundsCenter, boundsQuarterSize), wrap, 0);
        }
        //point intersection not needed 
        public void InsertPoint(T obj, float2 point)
        {
            var wrap = new ObjWrapper(obj, new AABB2D(point, point));
            var qsb = new QuarterSizeBounds(boundsCenter, boundsQuarterSize);
            int depth = 1; uint nodeId = 1;
            while (depth <= maxDepth)
            {
                int quadIndex = math.bitmask((point >= qsb.nodeCenter).xyxy) >> 2;
                qsb = QuarterSizeBounds.GetQuad(qsb, quadIndex);
                nodeId = GetQuadId(nodeId, quadIndex);
                if (TryInsert(nodeId, qsb, wrap, depth)) return;
                depth++;
            }
        }
        //recursively tries to insert into a child node..for a max of 4 child nodes, if the child nodes are full then subdivides
        //and goes deeper until it places the object into and empty node...8 is the max depth of the tree        
        void InsertNext(uint nodeId, in QuarterSizeBounds qsb, in ObjWrapper wrap, int parentDepth)
        {
            parentDepth++;
            int objMask = GetBoundsMask(qsb.nodeCenter, wrap.bounds);
            for (int i = 0; i < 4; i++)
            {
                int quadMask = QuadMasks[i];
                if ((objMask & quadMask) != quadMask) continue;

                uint childId = GetQuadId(nodeId, i);
                var childQ = QuarterSizeBounds.GetQuad(qsb, i);

                if (!TryInsert(childId, childQ, wrap, parentDepth))
                    InsertNext(childId, childQ, wrap, parentDepth);
            }
        }
        //checks if the nodes are full (4 max) if full goes subdivides..else returns true for an empty space
        //if there are no deeper nodes to go to (cant subdivide ) then the objectr force inserts itself at that level
        //which means that at deepest level it just checks for all possible nodes as its the final leaf
        //at the deepest level it’s just a flat list of objects that all share the same small region of space.
        //The tree no longer splits further; queries simply brute-force over that leaf’s contents. 
        bool TryInsert(uint nodeId, in QuarterSizeBounds qsb, in ObjWrapper wrap, int depth)
        {
            nodes.TryGetValue(nodeId, out int count);

            if (count <= objectsPerNode || depth == maxDepth)
            {
                count++;
                objects.Add(nodeId, wrap);
                nodes[nodeId] = count;

                if (count > objectsPerNode && depth < maxDepth)
                    Subdivide(nodeId, qsb, depth);

                return true;
            }
            return false;
        }

        void Subdivide(uint nodeId, in QuarterSizeBounds qsb, int depth)
        {
            int count = 0;
            var tmp = new NativeArray<ObjWrapper>(objectsPerNode + 1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            foreach (var o in objects.GetValuesForKey(nodeId)) tmp[count++] = o;

            var perQuad = new FixedList32Bytes<int> { Length = 4 };
            objects.Remove(nodeId);

            for (int i = 0; i < count; i++)
            {
                var o = tmp[i];
                int mask = GetBoundsMask(qsb.nodeCenter, o.bounds);
                for (int q = 0; q < 4; q++)
                {
                    int quadMask = QuadMasks[q];
                    if ((mask & quadMask) == quadMask)
                    {
                        objects.Add(GetQuadId(nodeId, q), o);
                        perQuad[q] = perQuad[q] + 1;
                    }
                }
            }
            tmp.Dispose();

            depth++;
            for (int q = 0; q < 4; q++)
            {
                int c = perQuad[q];
                if (c <= 0) continue;

                uint childId = GetQuadId(nodeId, q);
                nodes[childId] = c;

                if (c > objectsPerNode && depth < maxDepth)
                    Subdivide(childId, QuarterSizeBounds.GetQuad(qsb, q), depth);
            }
        }

        //Gizmos not used for now 
        public void DrawGizmos()
        {
            var root = new ExtentsBounds(boundsCenter, boundsExtents);
            GizmosNext(1, root, 0);
        }

        void GizmosNext(uint nodeId, in ExtentsBounds eb, int parentDepth)
        {
            parentDepth++;
            for (int i = 0; i < 4; i++)
            {
                uint childId = GetQuadId(nodeId, i);
                if (nodes.TryGetValue(childId, out int count))
                    Gizmos(childId, ExtentsBounds.GetQuad(eb, i), count, parentDepth);
            }
        }

        void Gizmos(uint nodeId, in ExtentsBounds eb, int objectCount, int depth)
        {
            if (objectCount <= objectsPerNode || depth == maxDepth)
            {
                UnityEngine.Gizmos.DrawWireCube((Vector2)eb.nodeCenter, (Vector2)eb.nodeExtents * 2);
                return;
            }
            GizmosNext(nodeId, eb, depth);
        }
    }
}
