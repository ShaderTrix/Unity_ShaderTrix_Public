using UnityEngine;
using UnityEngine.Animations.Rigging;

public class HipSweepTest : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private MultiAimConstraint spineIk;
    [Header("Sweep")]
    [SerializeField] private float sweepDistance = 0.4f;
    [SerializeField] private float maxDistance = 0.6f;

    [Header("Blend")]
    [SerializeField] private float aimInSpeed = 6f;
    [SerializeField] private float aimOutSpeed = 4f;

    [Header("Release")]
    [SerializeField] private float minContactTime = 0.15f;

    [SerializeField] private Collider[] includeColliders;

    bool hasContact;
    Transform surface;
    Vector3 localHitPoint;
    Vector3 hitNormal;
    float contactCooldown;

    void LateUpdate()
    {
        contactCooldown -= Time.deltaTime;

        float targetWeight = 0f;

        if (!hasContact)
            TryAcquire();

        if (hasContact)
            Maintain(ref targetWeight);

        var blend =  Mathf.MoveTowards(
            spineIk.weight,
            targetWeight,
            Time.deltaTime * (targetWeight > spineIk.weight ? aimInSpeed : aimOutSpeed)
        );     
        spineIk.weight = Mathf.Clamp(blend,0,0.5f);
    }

    void TryAcquire()
    {
        if (contactCooldown > 0f)
            return;

        if (rb.velocity.sqrMagnitude < 0.01f)
            return;

        RaycastHit hit;
        if (!rb.SweepTest(transform.forward, out hit, sweepDistance, QueryTriggerInteraction.Ignore))
            return;

        if (!IsIncluded(hit.collider))
            return;

        Debug.Log("Acquired contact");
        float dist = Vector3.Distance(spineIk.data.constrainedObject.position, hit.point);
        if (dist > maxDistance)
            return;

        surface = hit.collider.transform;
        localHitPoint = surface.InverseTransformPoint(hit.point);
        hitNormal = hit.normal;
        hasContact = true;
        contactCooldown = minContactTime;
    }

    void Maintain(ref float weight)
    {
        if (surface == null)
        {
            Release();
            return;
        }

        Vector3 worldPoint = surface.TransformPoint(localHitPoint);
        float dist = Vector3.Distance(spineIk.data.constrainedObject.position, worldPoint);

        if (dist > maxDistance)
        {
            Release();
            return;
        }

        Vector3 aimDir = -hitNormal;
        Vector3 up = transform.up;

        spineIk.data.sourceObjects[0].transform.position = spineIk.data.constrainedObject.position + aimDir * 0.5f;
        spineIk.data.sourceObjects[0].transform.rotation = Quaternion.LookRotation(aimDir, up);

        weight = 1f;
    }

    void Release()
    {
        hasContact = false;
        surface = null;
        contactCooldown = minContactTime;
    }

    bool IsIncluded(Collider c)
    {
        foreach (var ic in includeColliders)
            if (c == ic) return true;
        return false;
    }
}
