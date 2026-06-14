using System.Collections.Generic;
using UnityEngine;

public class UnitsManager : MonoBehaviour
{
    public FlowField_GridController _gridController;
    public int _numUnitPerSpawn = 5;
    public float _moveSpeed = 3f;
    public Material _material;

    private List<GameObject> _unitInGame = new();

    private void Update()
    {
        if (Input.GetMouseButtonDown(1))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hitInfo, 1000f))
            {
                SpawnUnits(hitInfo.point);
            }
        }
    }

    private void FixedUpdate()
    {
        if (_gridController._curFlowField == null) return;

        FlowField_Cell destination = _gridController._curFlowField._destinationCell;
        foreach (GameObject unit in new List<GameObject>(_unitInGame))
        {
            if (unit == null) continue;

            FlowField_Cell nodeBelow = _gridController._curFlowField.GetCellFromWorldPos(unit.transform.position);
            Vector3 moveDirection = new Vector3(nodeBelow._bestDirection._vector.x, 0, nodeBelow._bestDirection._vector.y);
            Rigidbody unitRB = unit.GetComponent<Rigidbody>();
            unitRB.velocity = moveDirection * _moveSpeed;

            // Destroy when near the destination
            float dist = Vector3.Distance(unit.transform.position, destination._worldPos);
            if (dist <= _gridController._cellRadius)
            {
                _unitInGame.Remove(unit);
                Destroy(unit);
            }
        }
    }

    private void SpawnUnits(Vector3 spawnPosition)
    {
        for (int i = 0; i < _numUnitPerSpawn; i++)
        {
            Vector3 offset = new Vector3(Random.Range(-0.3f, 0.3f), 0.5f, Random.Range(-0.3f, 0.3f));
            GameObject newUnit = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            newUnit.transform.position = spawnPosition + offset;
            newUnit.transform.localScale = Vector3.one * 0.25f; // smaller spheres look nicer
            newUnit.transform.SetParent(transform);

            if (_material != null)
            {
                Renderer rend = newUnit.GetComponent<Renderer>();
                rend.material = _material;
            }
            Rigidbody rb = newUnit.AddComponent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.FreezeRotation; // keep upright

            _unitInGame.Add(newUnit);
        }
    }
}
