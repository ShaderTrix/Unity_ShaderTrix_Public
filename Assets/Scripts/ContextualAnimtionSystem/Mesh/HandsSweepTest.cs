using UnityEngine;
using UnityEngine.Animations.Rigging;

public class HandsSweepTest : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Transform spineBone;
    [SerializeField] private TwoBoneIKConstraint ik;
    [SerializeField] private MultiAimConstraint spineIk;
    private Transform shoulder => ik.data.root.transform;
    private Transform hand => ik.data.tip.transform;

    [Header("Acquisition Cone")]
    [SerializeField] private float horizontalAngle = 60f; // degrees
    [SerializeField] private float verticalAngle = 45f;   // optional

    [Header("Sweep Settings")]
    [SerializeField] private float sweepDistance = 0.3f;
    [SerializeField] private float maxReach = 0.6f;

    [Header("Release")]
    [SerializeField] private float releaseDistance = 0.25f;
    [SerializeField] private float minContactTime = 0.15f;

    [Header("Side")]
    [SerializeField] private bool isRightHand = true;

    [SerializeField]
    private Collider[] _includeColiders;
    private bool hasContact;
    private Transform surface;
    private Vector3 localPoint;
    private Vector3 normal;
    private float contactCooldown;
    private float acquireCooldown;
    private Vector3 contactTangent;
    private Quaternion handBoneCorrection;
    private Vector3 handBoneCorrectionEuler = new Vector3(-90, 0, 0);

    void Awake()
    {
        handBoneCorrection = Quaternion.Euler(handBoneCorrectionEuler);
    }


    void LateUpdate()
    {

        contactCooldown -= Time.deltaTime;
        acquireCooldown -= Time.deltaTime;

        float handIKWeight = 0f;

        if (!hasContact)
            TryAcquireContact();

        if (hasContact)
            MaintainContact(ref handIKWeight);


        var blend = Mathf.MoveTowards(ik.weight, handIKWeight, Time.deltaTime * 3f);
        float spineBlend = Mathf.Clamp(blend, 0f, 0.35f);

        ik.weight = blend;

        var sources = spineIk.data.sourceObjects;
        WeightedTransform left = sources[0];
        WeightedTransform right = sources[1];
        if (isRightHand)
        {
            left.weight = spineBlend;
        }
        else
        {
            right.weight = spineBlend;
        }
        sources[0] = left;
        sources[1] = right;
        spineIk.data.sourceObjects = sources;
        spineIk.data = spineIk.data;
    }
    bool IsInsideHorizontalCone(Vector3 localDir)
    {
        Vector2 flat = new Vector2(localDir.x, localDir.z).normalized;

        float angle = Vector2.SignedAngle(Vector2.up, flat);

        if (isRightHand)
            return angle >= 0f && angle <= horizontalAngle;
        else
            return angle <= 0f && angle >= -horizontalAngle;
    }
    bool IsInsideVerticalCone(Vector3 localDir)
    {
        float angle = Vector3.Angle(
            Vector3.ProjectOnPlane(localDir, Vector3.right),
            Vector3.forward
        );

        return angle <= verticalAngle;
    }

    void TryAcquireContact()
    {
        if (acquireCooldown > 0f)
            return;

        Vector3 velocity = rb.velocity;
        if (velocity.sqrMagnitude < 0.01f)
            return;

        Vector3 toHand = hand.position - spineBone.position;
        Vector3 localDir = transform.InverseTransformDirection(toHand.normalized);

        if (!IsInsideHorizontalCone(localDir))
            return;

        if (!IsInsideVerticalCone(localDir))
            return;

        RaycastHit hit;
        if (rb.SweepTest(toHand.normalized, out hit, sweepDistance, QueryTriggerInteraction.Ignore))
        {
            if (!IsIncluded(hit.collider)) return;

            Vector3 hitPoint = hit.point;
            // Debug.Log("Acquired contact");
            if (Vector3.Distance(shoulder.position, hitPoint) > maxReach)
                return;

            surface = hit.collider.transform;
            localPoint = surface.InverseTransformPoint(hitPoint);
            normal = hit.normal;
            hasContact = true;
            contactCooldown = minContactTime;

            Vector3 rawTangent = Vector3.ProjectOnPlane(transform.forward, normal);
            if (rawTangent.sqrMagnitude < 0.001f)
                rawTangent = Vector3.Cross(normal, transform.right);

            contactTangent = rawTangent.normalized;
        }
    }
    void MaintainContact(ref float handIKWeight)
    {
        // Vector3 worldPoint = surface.TransformPoint(localPoint);

        // Vector3 toSurface = worldPoint - hand.position;
        // float dist = toSurface.magnitude;

        // if (dist < 0.001f)
        // {
        //     ReleaseContact();
        //     return;
        // }

        // RaycastHit hit;
        // if (!Physics.Raycast(
        //         hand.position,
        //         toSurface.normalized,
        //         out hit,
        //         dist + 0.02f,
        //         ~0,
        //         QueryTriggerInteraction.Ignore))
        // {
        //     ReleaseContact();
        //     return;
        // }

        // if (hit.collider.transform != surface)
        // {
        //     ReleaseContact();
        //     return;
        // }

        // // Update contact point & normal (important at corners)
        // worldPoint = hit.point;
        // normal = hit.normal;
        // localPoint = surface.InverseTransformPoint(worldPoint);

        Vector3 worldPoint = surface.TransformPoint(localPoint);

        Vector3 dir = (hand.position - spineBone.position).normalized;
        Vector3 localDir = transform.InverseTransformDirection(dir);
        if (contactCooldown <= 0f)
        {
            if (isRightHand && localDir.x < 0f)
            {
                ReleaseContact();
                return;
            }
            if (!isRightHand && localDir.x > 0f)
            {
                ReleaseContact();
                return;
            }
        }

        Vector3 animatedHand = hand.position;
        Vector3 offset = animatedHand - worldPoint;

        Vector3 sProj = offset - Vector3.Dot(offset, normal) * normal;//vproj = ab - dot(ab,ba)/dot(ba,ba) * ba

        Vector3 targetPos = worldPoint + sProj;

        if (Vector3.Distance(shoulder.position, targetPos) > maxReach)
        {
            TryRelease();
            return;
        }
        ik.data.target.position = targetPos;

        // Vector3 tangent = Vector3.ProjectOnPlane(hand.forward, -normal).normalized;
        // Quaternion rot = Quaternion.LookRotation(tangent,-normal);
        // Quaternion a = Quaternion.Euler(-90 * Vector3.right);

        // ik.data.target.rotation = rot * a;
        Vector3 palmNormal = -normal;
        Vector3 forward = Vector3.ProjectOnPlane(contactTangent, palmNormal).normalized;

        Quaternion contactRotation = Quaternion.LookRotation(forward, palmNormal);
        ik.data.target.rotation = contactRotation * handBoneCorrection;

        handIKWeight = 1.0f;
    }

    void TryRelease()
    {
        if (!hasContact || surface == null)
            return;

        if (contactCooldown > 0f)
            return;

        Vector3 worldPoint = surface.TransformPoint(localPoint);

        if (Vector3.Distance(hand.position, worldPoint) > releaseDistance)
        {
            ReleaseContact();
        }
    }
    private bool IsIncluded(Collider c)
    {
        foreach (var ic in _includeColiders)
            if (c == ic) return true;

        return false;
    }
    void ReleaseContact()
    {
        hasContact = false;
        surface = null;
        acquireCooldown = minContactTime;
    }
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isRightHand ? Color.red : Color.blue;

        Vector3 origin = spineBone.position;
        Vector3 fwd = transform.forward;

        Quaternion left = Quaternion.AngleAxis(
            isRightHand ? 0f : -horizontalAngle,
            transform.up
        );

        Quaternion right = Quaternion.AngleAxis(
            isRightHand ? horizontalAngle : 0f,
            transform.up
        );

        Gizmos.DrawRay(origin, left * fwd * 0.6f);
        Gizmos.DrawRay(origin, right * fwd * 0.6f);

    }
}
