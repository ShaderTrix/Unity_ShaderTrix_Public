using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class FlowField_GridController : MonoBehaviour
{
    public enum FlowFieldDisplayType
    {
        CostField,
        IntegrationField,
        VectorField,
    };
    public Vector2Int _gridSize;
    public float _cellRadius = 0.5f;
    public FlowField _curFlowField;
    [SerializeField] private bool _displayGrid = false;
    [SerializeField] private FlowFieldAgentComputeHelper _computeHelper;
    [SerializeField] private FlowFieldDisplayType _displayType;
    [SerializeField] private Collider[] _impassableTerrain;
    [SerializeField] private Collider[] _roughTerrain;
    [SerializeField] private Transform _goal;
    FlowField_Cell destinationCell;
    private Vector2Int lastGoalIndex = new(-1, -1);
    private void InitialiseGrid()
    {
        _curFlowField = new FlowField(_cellRadius, _gridSize);
        _curFlowField.CreateGrid();
    }
    private void Start()
    {
        InitialiseGrid();
        _curFlowField.CreateCostField(_impassableTerrain, _roughTerrain);

    }    

    private void Update()
    {
        Vector2Int newIndex = _curFlowField.GetCellFromWorldPos(_goal.position)._gridIndex;
        if (newIndex != lastGoalIndex)
        {
            lastGoalIndex = newIndex;
            
            // RebuildFlowFielJob(newIndex);
            RebuildFlowField();
        }
    }
    private void RebuildFlowField()
    {
        // _curFlowField.CreateCostField(_impassableTerrain, _roughTerrain);
        _curFlowField.CreateIntegrationField(_curFlowField.GetCellFromWorldPos(_goal.position));
        _curFlowField.CreateFlowField();
        _computeHelper.BakeAndUploadFlowField();
    }
    // private void RebuildFlowFielJob(Vector2Int goal)
    // {
    //    _curFlowField.UploadCostFieldToNative();     // alloc + fill once
    //     var native = _curFlowField.FlowFieldJob(goal); // schedule + complete
    //     _computeHelper.BakeAndUploadNativeFlowField(native);
    // }

    private void OnDrawGizmos()
    {
        if (_displayGrid)
        {
            if (_curFlowField == null) DrawGridGizmo(_gridSize, Color.black, _cellRadius);
            else DrawGridGizmo(_gridSize, Color.green, _cellRadius);
        }
    }
    private void DrawGridGizmo(Vector2Int drawGridSize, Color drawColor, float drawCellRadius)
    {
        Gizmos.color = drawColor;
        for (int x = -drawGridSize.x / 2; x < drawGridSize.x / 2; x++)
        {
            for (int y = -drawGridSize.y / 2; y < drawGridSize.y / 2; y++)
            {
                Vector3 wPos = new Vector3(drawCellRadius * 2 * x + drawCellRadius
                                           , 0,
                                           drawCellRadius * 2 * y + drawCellRadius);
                Vector3 size = Vector3.one * drawCellRadius * 2;
                Gizmos.DrawWireCube(wPos, size);
            }
        }

        if (!Application.isPlaying) return;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontStyle = FontStyle.BoldAndItalic;
        style.alignment = TextAnchor.MiddleCenter;

        switch (_displayType)
        {
            case FlowFieldDisplayType.CostField:
                foreach (FlowField_Cell curCell in _curFlowField.grid)
                {
                    Handles.Label(curCell._worldPos, curCell._cost.ToString(), style);
                }
                break;
            case FlowFieldDisplayType.IntegrationField:
                foreach (FlowField_Cell cell in _curFlowField.grid)
                {
                    string label = (cell._bestCost == ushort.MaxValue) ? "X" : cell._bestCost.ToString();
                    Handles.Label(cell._worldPos, label, style);
                }
                break;
            case FlowFieldDisplayType.VectorField:
                foreach (FlowField_Cell cell in _curFlowField.grid)
                {
                    DrawVectorFieldDebug(cell);
                }
                break;
            default:
                break;
        }
    }
    private void DrawVectorFieldDebug(FlowField_Cell cell)
    {
        if (cell == null) return;
        Vector3 pos = cell._worldPos + Vector3.up * 0.05f; // lift slightly above ground
        float size = _cellRadius * 0.8f;

        // Goal cell
        if (cell._bestCost == 0)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(pos, size * 0.5f);
            return;
        }

        // Impassable
        if (cell._cost == byte.MaxValue)
        {
            Gizmos.color = Color.red;
            DrawX(pos, size * 0.6f);
            return;
        }

        // Draw direction arrow
        Gizmos.color = Color.red;
        Vector3 dir = Vector3.zero;

        if (cell._bestDirection == FlowField_GridDirections.North) dir = Vector3.forward;
        else if (cell._bestDirection == FlowField_GridDirections.South) dir = Vector3.back;
        else if (cell._bestDirection == FlowField_GridDirections.East) dir = Vector3.right;
        else if (cell._bestDirection == FlowField_GridDirections.West) dir = Vector3.left;
        else if (cell._bestDirection == FlowField_GridDirections.NorthEast) dir = (Vector3.forward + Vector3.right).normalized;
        else if (cell._bestDirection == FlowField_GridDirections.NorthWest) dir = (Vector3.forward + Vector3.left).normalized;
        else if (cell._bestDirection == FlowField_GridDirections.SouthEast) dir = (Vector3.back + Vector3.right).normalized;
        else if (cell._bestDirection == FlowField_GridDirections.SouthWest) dir = (Vector3.back + Vector3.left).normalized;

        if (dir != Vector3.zero)
        {
            DrawArrow(pos, dir, size);
        }
    }
    private void DrawArrow(Vector3 pos, Vector3 dir, float length)
    {
        Vector3 end = pos + dir.normalized * length * 0.7f;
        Gizmos.DrawLine(pos, end);

        // Arrowhead
        Vector3 right = Quaternion.LookRotation(dir) * Quaternion.Euler(0, 150, 0) * Vector3.forward;
        Vector3 left  = Quaternion.LookRotation(dir) * Quaternion.Euler(0, -150, 0) * Vector3.forward;

        Gizmos.DrawLine(end, end + right * length * 0.3f);
        Gizmos.DrawLine(end, end + left * length * 0.3f);
    }

    private void DrawX(Vector3 pos, float size)
    {
        Vector3 offset = new Vector3(size, 0, size);
        Gizmos.DrawLine(pos - offset, pos + offset);
        Gizmos.DrawLine(pos - new Vector3(size, 0, -size), pos + new Vector3(size, 0, -size));
    }
}
