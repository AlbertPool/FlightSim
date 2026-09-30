using UnityEngine;

public class CameraScript : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField, Min(0.01f)] private float positionSmoothTime = 0.01f;
    [SerializeField, Min(0f)] private float rotationFollowSpeed = 5f;

    private Vector3 targetLocalPosition;
    private Quaternion targetLocalRotation;
    private Vector3 positionVelocity;

    void Awake()
    {
        if (target == null)
        {
            PlaneScript plane = GetComponentInParent<PlaneScript>();
            if (plane != null)
            {
                target = plane.transform;
            }
        }

        if (target == null)
        {
            Debug.LogWarning("CameraScript needs a plane target.", this);
            enabled = false;
            return;
        }

        targetLocalPosition = target.InverseTransformPoint(transform.position);
        targetLocalRotation = Quaternion.Inverse(target.rotation) * transform.rotation;

        if (transform != target && transform.IsChildOf(target))
        {
            transform.SetParent(null, true);
        }
    }

    void LateUpdate()
    {
        Vector3 desiredPosition = target.TransformPoint(targetLocalPosition);
        Quaternion desiredRotation = target.rotation * targetLocalRotation;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref positionVelocity,
            positionSmoothTime);

        float rotationBlend = 1f - Mathf.Exp(-rotationFollowSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationBlend);
    }
}
