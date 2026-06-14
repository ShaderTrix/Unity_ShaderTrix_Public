using System.Collections;
using UnityEngine;

public class ShootDecal : MonoBehaviour
{
    [SerializeField] private VisceraDecalPaintingComputeHelper _decalBaker;
    [SerializeField] private Camera cam;
    [SerializeField] private GameObject decalPrefab;
    [SerializeField] private float maxDistance = 50f;
    [SerializeField] private LayerMask hitMask;
    [SerializeField] private float surfaceOffset = 0.01f;
    [SerializeField] private Material  _mat;
    [SerializeField] private int _textureInt;
    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Shoot();
        }
    }

    void Shoot()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, hitMask))
            return;

        SpawnDecal(hit, ray.direction);

        SpawnBacksideDecal(hit, ray.direction);
    }


    void SpawnDecal(RaycastHit hit, Vector3 rayDirection)
    {
        Vector3 position = hit.point + hit.normal * surfaceOffset;
        // Vector3 forward = -hit.normal;
        Vector3 forward = cam.transform.forward;

        Vector3 camUp = cam.transform.up;
        Vector3 up = Vector3.ProjectOnPlane(forward, hit.normal).normalized;
        // Vector3 up = Vector3.ProjectOnPlane(-rayDirection, hit.normal).normalized;
        if (up.sqrMagnitude < 0.001f)
            up = Vector3.up;

        Quaternion rotation = Quaternion.LookRotation(forward, up);

        float scale = 1.0f;
        Texture2D tex = null;
        if (hit.collider.TryGetComponent(out DecalSizeHandler zoneTag)) 
        {
             scale = zoneTag.GetZoneScale(); 
             tex = zoneTag.GetZoneTexture(_textureInt);
        }
        StartCoroutine(BakeDecal(position, rotation, scale,tex));
    }
    void SpawnBacksideDecal(RaycastHit hit, Vector3 direction)
    {
       // Move slightly past the hit surface
        Vector3 start = hit.point + direction * 0.01f;

        float remainingDistance = maxDistance - hit.distance;

        if (remainingDistance <= 0f)
            return;

        if (!Physics.Raycast(start, direction, out RaycastHit backHit, remainingDistance, hitMask))
            return;

        SpawnDecal(backHit, direction);
    }

    private IEnumerator BakeDecal(Vector3 pos, Quaternion rot, float scale,Texture2D tex)
    {
        GameObject decalInstance = Instantiate(decalPrefab, pos, rot);
        decalInstance.transform.localScale = Vector3.one * scale;
        _mat.SetTexture("_MainTex",tex);

        yield return new WaitForEndOfFrame();
        _decalBaker._bakeDecal = true;
        yield return new WaitForEndOfFrame();
        Destroy(decalInstance);
    }
}
