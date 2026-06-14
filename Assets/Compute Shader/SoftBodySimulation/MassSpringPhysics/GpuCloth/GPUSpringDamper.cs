using System.Runtime.InteropServices;

namespace SoftBodySimulation
{   
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUSpringDamper
    {
        public readonly uint OtherNodeID;
        public readonly float DesiredDistance;
        public float SpringConstant;
        public GPUSpringDamper(uint otherNodeID, float desiredDistance, float springConstant)
        {
            OtherNodeID = otherNodeID;
            DesiredDistance = desiredDistance;
            SpringConstant = springConstant;
        }
    }
}
