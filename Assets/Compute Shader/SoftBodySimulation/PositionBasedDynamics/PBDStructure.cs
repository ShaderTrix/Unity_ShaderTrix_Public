using UnityEngine;
using System.Runtime.InteropServices;

namespace SoftBodySimulation
{
    [StructLayout (LayoutKind.Sequential)]
    public struct PBDNode
    {
        public Vector3 position;
        public Vector3 prevPosition;
        public float decay;
        public int pinned;
    }

    [StructLayout (LayoutKind.Sequential)]
    public struct PBDDamper {
        public int startNode;
        public int endNode;
        public float length;
        public PBDDamper(int a, int b, float len)
        {
            this.startNode = a;
            this.endNode = b;
            this.length = len;
        }
    }  
    [StructLayout (LayoutKind.Sequential)]
    public struct PBDPin
    {
        public int active;
        public int pinID;
        public Vector3 position;
        public PBDPin(int active,int id,Vector3 position)
        {
            this.active = active;
            this.pinID = id;            
            this.position = position;
        }
    }
}