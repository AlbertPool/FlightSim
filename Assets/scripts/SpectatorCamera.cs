using UnityEngine;

public class SpectatorCamera : MonoBehaviour
{
    [SerializeField] private Transform target;

    [Header("Wanneer verspringen")]
    [SerializeField] private float maxDistance = 120f;

    [Header("Nieuwe positie (t.o.v. het vliegtuig)")]
    [SerializeField] private float aheadDistance = 80f;   // hoe ver vóór het vliegtuig
    [SerializeField] private float sideDistance = 30f;    // hoe ver opzij
    [SerializeField] private float height = 5f;           // hoogte t.o.v. het vliegtuig
    [SerializeField] private float minHeightAboveGround = 3f;

    [Header("Meedraaien")]
    [SerializeField] private float lookSpeed = 4f;

    void Start()
    {
        if (target == null)
        {
            PlaneScript plane = FindFirstObjectByType<PlaneScript>();
            if (plane != null) target = plane.transform;
        }

        if (target == null)
        {
            Debug.LogWarning("SpectatorCamera needs a plane target.", this);
            enabled = false;
            return;
        }

        MoveToNewPosition();
    }

    void LateUpdate()
    {
        if (Vector3.Distance(transform.position, target.position) > maxDistance)
        {
            MoveToNewPosition();
            return;
        }

        Quaternion lookRotation = Quaternion.LookRotation(target.position - transform.position);
        float blend = 1f - Mathf.Exp(-lookSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, blend);
    }

    void MoveToNewPosition()
    {
        // Vliegrichting zonder pitch, zodat de camera niet de grond in of de lucht in schiet
        Vector3 forward = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, forward);
        float sideSign = Random.value < 0.5f ? -1f : 1f;   // willekeurig links of rechts

        Vector3 position = target.position
            + forward * aheadDistance
            + side * sideDistance * sideSign
            + Vector3.up * height;

        // Niet onder de grond
        if (Physics.Raycast(position + Vector3.up * 500f, Vector3.down, out RaycastHit hit, 1000f))
        {
            position.y = Mathf.Max(position.y, hit.point.y + minHeightAboveGround);
        }

        transform.position = position;
        transform.rotation = Quaternion.LookRotation(target.position - position);
    }
}
