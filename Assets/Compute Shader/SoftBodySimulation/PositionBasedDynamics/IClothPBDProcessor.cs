using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SoftBodySimulation
{
    public interface IClothPBDProcessor
    {
        public void SyncBonesOnCPU(Transform[] bones);
        public void InitializeShader();
        public void Step(Vector3 gravity,float decay = 1f);
        public void Pinned(Transform[] obj, float maxPinDist);
        public int FindNearestNodeCPU(Vector3 pos, float maxDistSqr);
        public void Gravity(Vector3 gravity);
        public void Solve();
        public void Dispose();
    }
}
