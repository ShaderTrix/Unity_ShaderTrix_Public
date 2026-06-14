using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

public class FlowField_Cell
{
    public Vector3 _worldPos;
    public Vector2Int _gridIndex;
    public byte _cost;
    public ushort _bestCost;
    public FlowField_GridDirections _bestDirection;
    //initialiser 
    public FlowField_Cell(Vector3 wsPos, Vector2Int gridID)
    {
        _worldPos = wsPos;
        _gridIndex = gridID;
        _cost = 1;
        _bestCost = ushort.MaxValue; //65535 = max for ushort
        _bestDirection = FlowField_GridDirections.None;
    }
    public void AddCost(int amount)
    {
        if (_cost == byte.MaxValue) return; //maxValue = 255
        if (amount + _cost > 255) { _cost = byte.MaxValue; }
        else { _cost += (byte)amount; }
    }
}
public struct FlowFieldCellNative
{
    public float3 worldPos;
    public int2 gridIndex;
    public byte cost;
    public ushort bestCost;
    public int bestDirection; // will hold enum index 0–8
}
public class FlowField_GridDirections
{
    public readonly Vector2Int _vector;
    private FlowField_GridDirections(int x, int y)
    {
        _vector = new Vector2Int(x, y);
    }
    public static implicit operator Vector2Int(FlowField_GridDirections dir)
    {
        return dir._vector;
    }
    public static FlowField_GridDirections GetDirectionfromVecToInt(Vector2Int vector)
    {
        return CardinalAndIntraCardinalDirections.DefaultIfEmpty(None).FirstOrDefault(x => x == vector);
    }
    public static readonly FlowField_GridDirections None = new FlowField_GridDirections(0, 0);
    public static readonly FlowField_GridDirections North = new FlowField_GridDirections(0, 1);
    public static readonly FlowField_GridDirections South = new FlowField_GridDirections(0, -1);
    public static readonly FlowField_GridDirections East = new FlowField_GridDirections(1, 0);
    public static readonly FlowField_GridDirections West = new FlowField_GridDirections(-1, 0);
    public static readonly FlowField_GridDirections NorthEast = new FlowField_GridDirections(1, 1);
    public static readonly FlowField_GridDirections NorthWest = new FlowField_GridDirections(-1, 1);
    public static readonly FlowField_GridDirections SouthEast = new FlowField_GridDirections(1, -1);
    public static readonly FlowField_GridDirections SouthWest = new FlowField_GridDirections(-1, -1);
    public static readonly List<FlowField_GridDirections> CardinalDirections = new List<FlowField_GridDirections>
    {
        North,
        East,
        South,
        West,
    };
    public static readonly List<FlowField_GridDirections> CardinalAndIntraCardinalDirections = new List<FlowField_GridDirections>
    {
        North,
        NorthEast,
        East,
        SouthEast,
        South,
        SouthWest,
        West,
        NorthWest,
    };
    public static readonly List<FlowField_GridDirections> AllDirections = new List<FlowField_GridDirections>
    {
        None,
        North,
        NorthEast,
        East,
        SouthEast,
        South,
        SouthWest,
        West,
        NorthWest,
    };
     // index order matches your AllDirections idea
    public static readonly int2[] AllDirectionsNative = new int2[8]
    {
        new int2(0, 1),    // N
        new int2(1, 1),    // NE
        new int2(1, 0),    // E
        new int2(1, -1),   // SE
        new int2(0, -1),   // S
        new int2(-1, -1),  // SW
        new int2(-1, 0),   // W
        new int2(-1, 1)    // NW
    };

    // matching movement costs
    public static readonly ushort[] MoveCostNative = new ushort[8]
    {
        10, 14, 10, 14, 10, 14, 10, 14 // diagonals = √2 * 10 ≈ 14
    };
    public static int EncodeDir(int2 dir)
    {
        for (int i = 0; i < AllDirectionsNative.Length; i++)
        {
            if (AllDirectionsNative[i].x == dir.x && AllDirectionsNative[i].y == dir.y)
                return i; // index 0–7
        }
        return -1; // none
    }

    public static FlowField_GridDirections DecodeDir(int dirIndex)
    {
        if (dirIndex < 0 || dirIndex >= AllDirectionsNative.Length) return None;
        return AllDirections[dirIndex + 1]; // AllDirections[0] == None
    }
}
