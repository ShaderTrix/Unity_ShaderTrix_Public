using System;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace SoftBodySimulation
{
    public sealed class PBDGPUProcessor : IDisposable, IClothPBDProcessor
    {
        private ComputeShader _shader;
        private int _kernelStepID, _kernelSolveID, _kernelPinnedID,_kernelGravityID;
        private int _nodeGroupSizeX,_damperGroupSizeX;
        public ComputeBuffer _NodesBuffer { get { return nodesBufferInput; } }
        public ComputeBuffer _DamperBuffer { get { return damperBuffer; } }
        public ComputeBuffer nodesBufferInput, nodesBufferOutput, damperBuffer, pinBuffer;
        public int nodesCount, damperCount;
        private PBDNode[] nodesInput;
        private PBDNode[] nodesOutput;
        private PBDPin[] pinArray;
        public PBDGPUProcessor(PBDNode[] nodes, PBDDamper[] dampers, PBDPin[] pins)
        {
            nodesInput = new PBDNode[nodes.Length];
            nodesOutput = new PBDNode[nodes.Length];
            pinArray = new PBDPin[pins.Length];

            Array.Copy(nodes, nodesInput, nodes.Length);
            Array.Copy(nodes, nodesOutput, nodes.Length);
            Array.Copy(pins, pinArray, pins.Length);

            nodesCount = nodes.Length;
            damperCount = dampers.Length;

            nodesBufferInput = new ComputeBuffer(nodesCount, Marshal.SizeOf<PBDNode>());
            nodesBufferOutput = new ComputeBuffer(nodesCount, Marshal.SizeOf<PBDNode>());
            pinBuffer = new ComputeBuffer(pinArray.Length, Marshal.SizeOf<PBDPin>());
            damperBuffer = new ComputeBuffer(damperCount, Marshal.SizeOf<PBDDamper>());

            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i].prevPosition = nodes[i].position;
            }
            nodesBufferInput.SetData(nodes);
            nodesBufferOutput.SetData(nodes);
            damperBuffer.SetData(dampers);
            pinBuffer.SetData(pinArray);

            InitializeShader();
        }       
        public void SyncBonesOnCPU(Transform[] bones)
        {
            nodesBufferOutput.GetData(nodesOutput);
            for (int i = 0; i < bones.Length; ++i)
            {
                bones[i].localPosition = nodesOutput[i].position;
            }
        }
        public void InitializeShader()
        {
            if (!_shader)
            {
                string[] guids = AssetDatabase.FindAssets("PBDCompute t:ComputeShader");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    _shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                }
            }
            _kernelSolveID = _shader.FindKernel("SolveKernel");
            _kernelStepID = _shader.FindKernel("StepKernel");
            _kernelPinnedID = _shader.FindKernel("PinnedKernel");
            _kernelGravityID = _shader.FindKernel("GravityKernel");

            _shader.GetKernelThreadGroupSizes(_kernelSolveID, out uint threadGroupSizeX, out _, out _);
            // _nodeGroupSizeX = Mathf.FloorToInt(nodesCount / (int)threadGroupSizeX) + 1; 
            // _damperGroupSizeX = Mathf.FloorToInt(damperCount / (int)threadGroupSizeX) + 1;

            //we are doing this becase nodeCount is an integer value and for integer division floor will be wrong
            //for floats it would make sence but for integers we do this
            //use this version only - its better
            _nodeGroupSizeX = (nodesCount + (int)threadGroupSizeX - 1) / (int)threadGroupSizeX;
            _damperGroupSizeX = (damperCount + (int)threadGroupSizeX - 1) / (int)threadGroupSizeX;

            _shader.SetInt("_NodesCount", nodesCount);
            _shader.SetInt("_DampersCount", damperCount);
        }
        public void Step(Vector3 gravity,float decay = 1f)
        {
            _shader.SetBuffer(_kernelStepID, "_NodesBufferInput", nodesBufferInput);  
            _shader.SetBuffer(_kernelStepID, "_NodesBufferOutput", nodesBufferOutput);  

            _shader.SetFloat("_Decay", decay);
            _shader.Dispatch(_kernelStepID, _nodeGroupSizeX, 1, 1);
            SwapBuffers(ref nodesBufferInput, ref nodesBufferOutput);
        }
        public void Pinned(Transform[] obj, float maxPinDist)
        {
            float maxDistSqr = maxPinDist * maxPinDist;

            for (int i = 0; i < pinArray.Length; ++i)
                pinArray[i].active = 0;

            nodesBufferInput.GetData(nodesOutput);
            foreach (var t in obj)
            {
                int id = FindNearestNodeCPU(t.localPosition, maxDistSqr);

                if (id >= 0)
                {
                    pinArray[id].active = 1;
                    pinArray[id].pinID = id;
                    pinArray[id].position = t.localPosition;

                    // for (int i = 0; i < dampersArray.Length; i++)
                    // {
                    //     if (dampersArray[i].startNode == id ||
                    //         dampersArray[i].endNode   == id)
                    //     {
                    //         var d = dampersArray[i];
                    //         dampersArray[i] = d;
                    //     }
                    // }
                    // damperBuffer.SetData(dampersArray);                    
                    // Debug.Log("## Pinning id:" + id + "at position:" + t.localPosition);
                }
            }
            pinBuffer.SetData(pinArray);
            _shader.SetBuffer(_kernelPinnedID, "_NodesBufferInput", nodesBufferInput);  
            _shader.SetBuffer(_kernelPinnedID, "_NodesBufferOutput", nodesBufferOutput);

            _shader.SetBuffer(_kernelPinnedID, "_PinBuffer", pinBuffer);         
            _shader.Dispatch(_kernelPinnedID, _nodeGroupSizeX, 1, 1);
            SwapBuffers(ref nodesBufferInput, ref nodesBufferOutput);
        }
        public int FindNearestNodeCPU(Vector3 pos, float maxDistSqr)
        {
            int bestIndex = -1;
            float best = maxDistSqr;

            for (int i = 0; i < nodesOutput.Length; ++i)
            {
                float d = (nodesOutput[i].position - pos).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        public void Gravity(Vector3 gravity)
        {
            _shader.SetBuffer(_kernelGravityID, "_NodesBufferInput", nodesBufferInput);  
            _shader.SetBuffer(_kernelGravityID, "_NodesBufferOutput", nodesBufferOutput);  

            _shader.SetVector("_Gravity", gravity);
            _shader.SetFloat("_DeltaTime", Time.deltaTime);

            _shader.Dispatch(_kernelGravityID, _nodeGroupSizeX, 1, 1);
            SwapBuffers(ref nodesBufferInput, ref nodesBufferOutput);
        }
        public void Solve()
        {
            _shader.SetBuffer(_kernelSolveID, "_NodesBufferInput", nodesBufferInput);
            _shader.SetBuffer(_kernelSolveID, "_NodesBufferOutput", nodesBufferOutput);
            _shader.SetBuffer(_kernelSolveID, "_DampersBuffer", damperBuffer);
            _shader.SetInt("_DampersCount", damperCount);

            _shader.Dispatch(_kernelSolveID, _damperGroupSizeX, 1, 1);

            SwapBuffers(ref nodesBufferInput, ref nodesBufferOutput);
        }

        // public void Solve()
        // {
        //     _shader.SetBuffer(_kernelSolveID, "_NodesBufferInput", nodesBufferInput);
        //     _shader.SetBuffer(_kernelSolveID, "_NodesBufferOutput", nodesBufferOutput);

        //     _shader.SetBuffer(_kernelSolveID, "_DampersBuffer", damperBuffer);
        //     _shader.SetInt("_DampersCount", damperCount);

        //     _shader.Dispatch(_kernelSolveID, _damperGroupSizeX, 1, 1);
        //     SwapBuffers(ref nodesBufferInput, ref nodesBufferOutput);
        // }
        private void SwapBuffers(ref ComputeBuffer a, ref ComputeBuffer b)
        {
            var tmp = a;
            a = b;
            b = tmp;
        }
        public void Dispose()
        {
            nodesBufferInput?.Release(); nodesBufferInput = null;
            nodesBufferOutput?.Release(); nodesBufferOutput = null;
            damperBuffer?.Release(); damperBuffer = null;
            pinBuffer?.Release(); pinBuffer = null;
        }
    }
}

