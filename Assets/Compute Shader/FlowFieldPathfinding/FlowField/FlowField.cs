using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class FlowField
{
    //array of vector2s... grid[x,y]
    public FlowField_Cell[,] grid { get; private set; }
    public Vector2Int _gridSize { get; private set; }
    public float _cellRadius { get; private set; }
    private float _cellDiameter;
    public FlowField_Cell _destinationCell;
    private Collider[] overlaps = new Collider[32];
    private HashSet<Collider> impassableSet = new();
    private HashSet<Collider> roughSet = new();
    private Queue<FlowField_Cell> cellsToCheck = new();
    private List<FlowField_Cell> currNeightbours = new();

    private NativeArray<FlowFieldCellNative> _nativeCells; //persistant array
    private int2 _gridSizeInt2;

    public void OnDestroy()
    {
        if (_nativeCells.IsCreated) _nativeCells.Dispose();
    }

    //initialiser
    public FlowField(float _radius, Vector2Int _size)
    {
        _cellRadius = _radius;
        _cellDiameter = _radius * 2;
        _gridSize = _size;
    }
    public void CreateGrid()
    {
        grid = new FlowField_Cell[_gridSize.x, _gridSize.y];

        int xOffset = -_gridSize.x / 2;//for centering the grid pivot
        int yOffset = -_gridSize.y / 2;

        for (int x = 0; x < _gridSize.x; x++)
        {
            for (int y = 0; y < _gridSize.y; y++)
            {
                int worldX = x + xOffset;
                int worldY = y + yOffset;

                Vector3 wPos = new Vector3(_cellDiameter * worldX + _cellRadius, 0, _cellDiameter * worldY + _cellRadius);
                grid[x, y] = new FlowField_Cell(wPos, new Vector2Int(x, y));
            }
        }
    }
    public void CreateCostField(IEnumerable<Collider> impassableTerrain, IEnumerable<Collider> roughTerrain)
    {
        Vector3 cellHalfExtents = Vector3.one * _cellRadius;
        //Similar to list or array but faster like a dictionary but without the keyvalue paring
        //Hashset have unordered sorting and dont allow duplicate entries..accessing using [0] is also not there but its faster and non compile time constant 
        impassableSet.Clear();
        roughSet.Clear();

        impassableSet.UnionWith(impassableTerrain);
        roughSet.UnionWith(roughTerrain);

        for (int x = 0; x < _gridSize.x; x++)
        {
            for (int y = 0; y < _gridSize.y; y++)
            {
                FlowField_Cell currCell = grid[x, y];
                int count = Physics.OverlapBoxNonAlloc(currCell._worldPos, cellHalfExtents, overlaps, Quaternion.identity);

                for (int i = 0; i < count; i++)
                {
                    Collider col = overlaps[i];
                    if (impassableSet.Contains(col))
                    {
                        currCell.AddCost(255);
                        goto NextCell;
                    }
                    else if (roughSet.Contains(col))
                    {
                        currCell.AddCost(3);
                        break;
                    }
                }

            NextCell:;
            }
        }
    }
    // public void InitJob()
    // {
    //     if (!_nativeCells.IsCreated || _nativeCells.Length != _gridSize.x * _gridSize.y)
    //     {
    //         if (_nativeCells.IsCreated)
    //             _nativeCells.Dispose();

    //         _nativeCells = new NativeArray<FlowFieldCellNative>(
    //             _gridSize.x * _gridSize.y,
    //             Allocator.Persistent,
    //             NativeArrayOptions.ClearMemory
    //         );
    //     }
    //     for (int y = 0; y < _gridSize.y; y++)
    //     {
    //         for (int x = 0; x < _gridSize.x; x++)
    //         {
    //             int i = y * _gridSize.x + x;
    //             var cell = grid[x, y];
    //             _nativeCells[i] = new FlowFieldCellNative
    //             {
    //                 cost = cell._cost,
    //                 gridIndex = new int2(x, y)
    //             };
    //         }
    //     }
    // }
    // public void UploadCostFieldToNative()
    // {
    //     if (_nativeCells.IsCreated) _nativeCells.Dispose();
    //     _gridSizeInt2 = new int2(_gridSize.x, _gridSize.y);
    //     _nativeCells = new NativeArray<FlowFieldCellNative>(_gridSize.x * _gridSize.y, Allocator.Persistent);
    //     for (int y = 0; y < _gridSize.y; y++)
    //     {
    //         for (int x = 0; x < _gridSize.x; x++)
    //         {
    //             int i = y * _gridSize.x + x;
    //             var src = grid[x, y];
    //             _nativeCells[i] = new FlowFieldCellNative { worldPos = src._worldPos, gridIndex = new int2(x, y), cost = src._cost, bestCost = src._bestCost, bestDirection = 0 };
    //         }
    //     }
    // }
    // [BurstCompile]
    // public struct IntegrationFieldJob : IJob
    // {
    //     public int2 gridSize;
    //     public NativeArray<FlowFieldCellNative> cells;
    //     public int2 destination;
    //     public void Execute()
    //     {
    //         //setting all cost to max value
    //         for (int i = 0; i < cells.Length; i++)
    //         {
    //             var cell = cells[i];
    //             cell.bestCost = ushort.MaxValue;
    //             cells[i] = cell;
    //         }
    //         //setting one cell to best cost = 0
    //         var id = destination.y * gridSize.x + destination.x;
    //         var dest = cells[id];
    //         dest.bestCost = 0;
    //         cells[id] = dest;

    //         var queue = new NativeQueue<int>(Allocator.Temp);
    //         queue.Enqueue(id);
    //         while (queue.Count > 0)
    //         {
    //             int currIndex = queue.Dequeue();
    //             FlowFieldCellNative curr = cells[currIndex];
    //             int2 currPos = curr.gridIndex;
    //             for (int i = 0; i < 8; i++)
    //             {
    //                 int2 dir = FlowField_GridDirections.AllDirectionsNative[i];
    //                 int2 neighbourPos = currPos + dir;

    //                 if (neighbourPos.x < 0 || neighbourPos.x >= gridSize.x ||
    //                     neighbourPos.y < 0 || neighbourPos.y >= gridSize.y)
    //                     continue;

    //                 int nIndex = neighbourPos.y * gridSize.x + neighbourPos.x;
    //                 FlowFieldCellNative n = cells[nIndex];

    //                 if (n.cost == byte.MaxValue) continue;

    //                 ushort newCost = (ushort)(curr.bestCost + n.cost + FlowField_GridDirections.MoveCostNative[i]);
    //                 if (newCost < n.bestCost)
    //                 {
    //                     n.bestCost = newCost;
    //                     cells[nIndex] = n;
    //                     queue.Enqueue(nIndex);
    //                 }
    //             }

    //         }
    //     }
    // }
    // [BurstCompile]
    // public struct FlowFieldDirectionJob : IJob
    // {
    //     [ReadOnly] public int2 gridSize;
    //     public NativeArray<FlowFieldCellNative> cells;
    //     public NativeArray<int> bestDirection;

    //     public void Execute()
    //     {
    //         for (int index = 0; index < cells.Length; index++)
    //         {
    //             var cell = cells[index];
    //             if (cell.cost == byte.MaxValue)
    //             {
    //                 bestDirection[index] = 0;
    //                 continue;
    //             }

    //             int2 coord = cell.gridIndex;
    //             ushort lowest = cell.bestCost;
    //             int2 bestDir = new int2(0, 0);

    //             for (int i = 0; i < FlowField_GridDirections.AllDirectionsNative.Length; i++)
    //             {
    //                 int2 dir = FlowField_GridDirections.AllDirectionsNative[i];
    //                 int2 nCoord = coord + dir;
    //                 if (nCoord.x < 0 || nCoord.x >= gridSize.x ||
    //                     nCoord.y < 0 || nCoord.y >= gridSize.y)
    //                     continue;

    //                 int nIndex = nCoord.y * gridSize.x + nCoord.x;
    //                 var n = cells[nIndex];
    //                 if (n.bestCost < lowest)
    //                 {
    //                     lowest = n.bestCost;
    //                     bestDir = dir;
    //                 }
    //             }

    //             cell.bestDirection = FlowField_GridDirections.EncodeDir(bestDir);
    //             cells[index] = cell;
    //             bestDirection[index] = cell.bestDirection;
    //         }
    //     }
    // }
    // public NativeArray<FlowFieldCellNative> FlowFieldJob(Vector2Int goal)
    // {
    //     // same as your FlowFieldJob() logic
    //     var jobA = new IntegrationFieldJob { gridSize = _gridSizeInt2, cells = _nativeCells, destination = new int2(goal.x, goal.y) };
    //     var handleA = jobA.Schedule();

    //     var jobB = new FlowFieldDirectionJob
    //     {
    //         gridSize = _gridSizeInt2,
    //         cells = _nativeCells,
    //         bestDirection = new NativeArray<int>(_nativeCells.Length, Allocator.TempJob)
    //     };
    //     var handleB = jobB.Schedule(handleA);
    //     handleB.Complete();

    //     jobB.bestDirection.Dispose();
    //     return _nativeCells;
    // }

    public void CreateIntegrationField(FlowField_Cell finalCell)
    {
        _destinationCell = finalCell;
        for (int x = 0; x < _gridSize.x; x++)
        {
            for (int y = 0; y < _gridSize.y; y++)
            {
                FlowField_Cell cell = grid[x, y];
                cell._bestCost = ushort.MaxValue;
            }
        }
        _destinationCell._bestCost = 0;

        //an kind of FIFO list where elemts added first are the first to go out when dequeue, 
        // so when i add 10 to the queue then 20 then when i dequeue it will remove 10 first 
        cellsToCheck.Clear();
        cellsToCheck.Enqueue(_destinationCell);

        while (cellsToCheck.Count > 0)
        {
            FlowField_Cell currCell = cellsToCheck.Dequeue();
            currNeightbours.Clear();
            GetNeighbourCells(currCell._gridIndex, FlowField_GridDirections.CardinalDirections, currNeightbours);
            for (int i = 0; i < currNeightbours.Count; i++)
            {
                FlowField_Cell curNeighbour = currNeightbours[i];
                if (curNeighbour._cost == byte.MaxValue) { continue; }
                if (curNeighbour._cost + currCell._bestCost < curNeighbour._bestCost)
                {
                    curNeighbour._bestCost = (ushort)(curNeighbour._cost + currCell._bestCost);
                    cellsToCheck.Enqueue(curNeighbour);
                }
            }
        }
    }
    private void GetNeighbourCells(Vector2Int nodeIndex, List<FlowField_GridDirections> directions, List<FlowField_Cell> neighbourCells)
    {
        for (int i = 0; i < directions.Count; i++)
        {
            Vector2Int curDir = directions[i];
            FlowField_Cell newNeighBour = GetCellAtRelativePosition(nodeIndex, curDir);
            if (newNeighBour != null)
                neighbourCells.Add(newNeighBour);
        }
    }
    private FlowField_Cell GetCellAtRelativePosition(Vector2Int originPos, Vector2Int relativePos)
    {
        Vector2Int finalPos = originPos + relativePos;
        if (finalPos.x < 0 || finalPos.x >= _gridSize.x || finalPos.y < 0 || finalPos.y >= _gridSize.y)
        {
            return null;
        }
        else return grid[finalPos.x, finalPos.y];
    }
    public FlowField_Cell GetCellFromWorldPos(Vector3 worldPos)
    {
        float originX = -_gridSize.x / 2f * _cellDiameter + _cellRadius;
        float originZ = -_gridSize.y / 2f * _cellDiameter + _cellRadius;

        float percentX = (worldPos.x - originX) / (_gridSize.x * _cellDiameter);
        float percentY = (worldPos.z - originZ) / (_gridSize.y * _cellDiameter);

        percentX = Mathf.Clamp01(percentX);
        percentY = Mathf.Clamp01(percentY);

        int x = Mathf.Clamp(Mathf.FloorToInt(_gridSize.x * percentX), 0, _gridSize.x - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(_gridSize.y * percentY), 0, _gridSize.y - 1);

        return grid[x, y];
    }
    public void CreateFlowField()
    {
        currNeightbours.Clear();
        for (int x = 0; x < _gridSize.x; x++)
        {
            for (int y = 0; y < _gridSize.y; y++)
            {
                FlowField_Cell curCell = grid[x, y];
                currNeightbours.Clear();
                GetNeighbourCells(curCell._gridIndex, FlowField_GridDirections.AllDirections, currNeightbours);

                int bestCost = curCell._bestCost;

                for (int i = 0; i < currNeightbours.Count; i++)
                {
                    FlowField_Cell curNeighbour = currNeightbours[i];
                    if (curNeighbour._bestCost < bestCost)
                    {
                        bestCost = curNeighbour._bestCost;
                        curCell._bestDirection = FlowField_GridDirections.GetDirectionfromVecToInt(
                            curNeighbour._gridIndex - curCell._gridIndex);
                    }
                }
            }
        }
    }
}
