using UnityEngine;

/// <summary>
/// Put this on every ring. It creates a trigger zone in the hole of the ring
/// and tells the RingManager when the plane flies through it.
/// </summary>
public class RingTrigger : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("Tag on the root of the plane (AirplaneRoot).")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Radius of the hit zone inside the ring (local units). Tweak until the yellow gizmo fills the hole.")]
    [SerializeField] private float hitRadius = 1.5f;

    [Tooltip("Offset of the hit zone if the ring's pivot isn't in the centre.")]
    [SerializeField] private Vector3 hitOffset = Vector3.zero;

    [Header("Feedback")]
    [Tooltip("Hide the ring after it's been hit.")]
    [SerializeField] private bool hideOnHit = true;
    [SerializeField] private AudioClip hitSound;

    public bool IsHit { get; private set; }

    private void Awake()
    {
        // Create a dedicated trigger so the ring's own (mesh) collider stays untouched.
        var zone = gameObject.AddComponent<SphereCollider>();
        zone.isTrigger = true;
        zone.radius = hitRadius;
        zone.center = hitOffset;
    }

    private void Start()
    {
        if (RingManager.Instance != null)
            RingManager.Instance.RegisterRing(this);
        else
            Debug.LogWarning("No RingManager in the scene!", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsHit) return;
        if (!other.transform.root.CompareTag(playerTag)) return;

        IsHit = true;

        if (hitSound != null)
            AudioSource.PlayClipAtPoint(hitSound, transform.position);

        if (hideOnHit)
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = false;

        RingManager.Instance?.RingHit(this);
    }

    // Shows the hit zone in the Scene view so you can size it.
    private void OnDrawGizmos()
    {
        Gizmos.color = IsHit ? Color.green : Color.yellow;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireSphere(hitOffset, hitRadius);
    }
}
